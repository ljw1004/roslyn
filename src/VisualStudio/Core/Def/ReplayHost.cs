using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class ReplayHost : IDisposable
{
    private Process Process;

    public void WatchAndMissing(string file, int line, int lineCount, IEnumerable<int> missing)
    {
        var s = missing == null ? "" : string.Join("\t", missing);
        if (s != "") s = "\tmissing\t" + s;
        Process.StandardInput.WriteLine($"watch\t{file}\t{line}\t{lineCount}{s}");
    }

    public async Task<Tuple<int, string>> ReadReplayAsync(CancellationToken cancel)
    {
        var lineTask = Process.StandardOutput.ReadLineAsync();
        var tcs = new TaskCompletionSource<object>();
        using (var reg = cancel.Register(tcs.SetCanceled)) await Task.WhenAny(lineTask, tcs.Task).ConfigureAwait(false);
        if (!lineTask.IsCompleted) return null;
        var line = await lineTask.ConfigureAwait(false);
        var separator = line.IndexOf(':');
        int i;
        if (separator == -1 || !int.TryParse(line.Substring(0, separator), out i)) return Tuple.Create(-1, $"host can't parse target's output '{line}'");
        return Tuple.Create(i, line.Substring(separator + 1));
    }

    public void Dispose()
    {
        if (Process != null && !Process.HasExited) { Process.Kill(); Process.WaitForExit(); }
        if (Process != null) Process.Dispose(); Process = null;
    }

    public static async Task<Project> InstrumentAsync(Project originalProject, CancellationToken cancel = default(CancellationToken))
    {
        var originalComp = await originalProject.GetCompilationAsync(cancel).ConfigureAwait(false);
        var replay = originalComp.GetTypeByMetadataName("System.Runtime.CompilerServices.Replay");

        var project = originalProject;
        foreach (var documentId in originalProject.DocumentIds)
        {
            var oldDocument = project.GetDocument(documentId);
            var oldTree = await oldDocument.GetSyntaxTreeAsync(cancel).ConfigureAwait(false);
            var oldRoot = await oldTree.GetRootAsync(cancel).ConfigureAwait(false);
            var rewriter = new TreeRewriter(originalComp, oldTree);
            var newRoot = rewriter.Visit(oldRoot);
            var newDocument = oldDocument.WithSyntaxRoot(newRoot);
            project = newDocument.Project;
        }

        return project;
    }

    public class BuildResult
    {
        public BuildResult(EmitResult emitResult, string outputFilePath)
        {
            Success = emitResult.Success;
            Diagnostics = emitResult.Diagnostics;
            ReplayOutputFilePath = outputFilePath;
        }

        public readonly bool Success;
        public readonly ImmutableArray<Diagnostic> Diagnostics;
        public readonly string ReplayOutputFilePath;
    }

    public static async Task<BuildResult> BuildAsync(Project project, CancellationToken cancel = default(CancellationToken))
    {
        // Pick a filename
        string fn = "";
        for (int i = 0; ; i++)
        {
            fn = Path.ChangeExtension(project.OutputFilePath, $".replay{(i == 0 ? "" : i.ToString())}.exe");
            if (File.Exists(fn)) try { File.Delete(fn); } catch (Exception) { }
            if (!File.Exists(fn)) break;
        }

        var comp = await project.GetCompilationAsync(cancel).ConfigureAwait(false);
        var result = await Task.Run(() => comp.Emit(fn, cancellationToken: cancel)).ConfigureAwait(false);
        return new BuildResult(result, fn);
    }

    public static async Task<ReplayHost> RunAsync(string outputFilePath, CancellationToken cancel = default(CancellationToken))
    {
        Process process = null;
        try
        {
            process = new Process();
            process.StartInfo.FileName = outputFilePath;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            //process.WaitForInputIdle();
            var lineTask = process.StandardOutput.ReadLineAsync();
            var tcs = new TaskCompletionSource<object>();
            using (var reg = cancel.Register(tcs.SetCanceled)) await Task.WhenAny(lineTask, tcs.Task).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();
            var line = await lineTask.ConfigureAwait(false);
            if (line != "OK") throw new Exception($"Expecting 'OK', got '{line}'");
            var host = new ReplayHost { Process = process };
            process = null;
            return host;
        }
        finally
        {
            if (process != null && !process.HasExited) { process.Kill(); process.WaitForExit(); }
            if (process != null) process.Dispose(); process = null;
        }
    }

}

class TreeRewriter : CSharpSyntaxRewriter
{
    Compilation Compilation;
    SyntaxTree OriginalTree;
    SemanticModel SemanticModel;
    INamedTypeSymbol ConsoleType;

    public TreeRewriter(Compilation compilation, SyntaxTree tree)
    {
        Compilation = compilation;
        OriginalTree = tree;
        SemanticModel = compilation.GetSemanticModel(tree);
        ConsoleType = Compilation.GetTypeByMetadataName("System.Console");
    }

    public override SyntaxNode VisitBlock(BlockSyntax node)
        => node.WithStatements(SyntaxFactory.List(ReplaceStatements(node.Statements)));

    public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node)
        => node.WithStatements(VisitList(SyntaxFactory.List(ReplaceStatements(node.Statements))));

    public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        if (node.Identifier.Text == "Replay") return node;
        else return base.VisitClassDeclaration(node);
    }

    IEnumerable<StatementSyntax> ReplaceStatements(IEnumerable<StatementSyntax> statements)
    {
        // LocalDeclarationStatement  int x, y=10                  -> int x,y=Log<type>(10,"y",...,1);
        // ExpressionStatement        f();                         -> Log(f(),null,...,2) or f();Log(null,null,...,3);
        //    PropertyDeclaration     int p <accessors>
        //    PropertyDeclaration     int p <accessors> = e;       -> int p <accessors> = Log<type>(e,"p",...,12);
        //    PropertyDeclaration     int p => e;                  -> int p => Log<type>(e,"p",...,12);
        //    MethodDeclaration       int f() => e;                -> void f() => Log<type?>(e,"f",...,14);
        //    MethodDeclaration       void f() => e;               -> ??
        // EmptyStatement             ;
        // LabeledStatement           fred: <stmt>;
        // GotoStatement              goto fred;
        // SwitchStatement            switch (e) { <sections> }    -> switch(Log(e,"e"?,...,4)) { <sections> }
        // GotoCaseStatement          goto case 2;
        // GotoDefaultStatement       goto default;
        // BreakStatement             break;
        // ContinueStatement          continue;
        // ReturnStatement            return e;                    -> return; or return Log<type>(e,"e"?,...,5);
        // YieldReturnStatement       yield return e;              -> yield return Log<type>(e,"e"?,...,6);
        // YieldBreakStatement        yield break;
        // ThrowStatement             throw e;                     -> throw Log(e,"e"?,...,7);
        // WhileStatement             while (e) <stmt>             -> while (Log(e,"e"?,...,8)) { <stmts> }
        // DoStatement                do <stmt> while (e);         -> do { <stmts> } while (Log(e,"e"?,...,9);
        // ForStatement               for (<exprstmts | decl>; <exprs>; <exprstmts>) <stmt>  -> >>
        // ForEachStatement           foreach (var x in e) <stmt>  -> ??
        // UsingStatement             using (<decl | expr>) <stmt> -> ??
        // FixedStatement             fixed (<decl>) <stmt>        -> fixed (<decl>) { <stmts> }
        // CheckedStatement           checked <block>
        // UncheckedStatement         unchecked <block>
        // UnsafeStatement            unsafe <block>
        // LockStatement              lock (<expr>) <stmt>         -> lock (<expr>) { <stmts> }
        // IfStatement                if (<expr>) <stmt> <else>    -> ??
        // TryStatement               try <block> catch (<decl>) when e <block> finally <block> -> ??
        // GlobalStatement            ??
        // Block                      { <stmts> }                  -> { Log(null,null,...,10); <stmts> }
        //    MethodDeclaration       void f(int i) <block>        -> ??
        //    MethodDeclaration       void f(int i) <errors!>      -> ??
        //    ConstructorDeclaration  C(int i) <block>             -> ??
        //    AccessorDeclaration     set <block>                  -> set { Log(value,"value",...,13); <stmts> }
        // FieldDeclaration           int p = e;                   -> int p = Log<type>(e,"p",...,15);

        foreach (var statement0 in statements)
        {
            var statement1 = Visit(statement0) as StatementSyntax;

            if (statement1.IsKind(SyntaxKind.LocalDeclarationStatement))
            {
                // int x, y=10 -> int x,y=Log<type>(10,"y",...,1);
                var statement = statement1 as LocalDeclarationStatementSyntax;
                var type = SemanticModel.GetSymbolInfo(statement.Declaration.Type).Symbol as ITypeSymbol;
                if (type == null) { yield return statement; continue; }
                var variables = statement.Declaration.Variables;
                var locs = variables.Select(v => v.GetLocation()).ToArray();
                for (int i = 0; i < variables.Count; i++)
                {
                    var oldVariable = variables[i];
                    if (oldVariable.Initializer == null) continue;
                    var id = oldVariable.Identifier.ValueText;
                    var newValue = SyntaxFactory_Log(type, oldVariable.Initializer.Value, id, locs[i], 1);
                    var newVariable = oldVariable.WithInitializer(oldVariable.Initializer.WithValue(newValue));
                    variables = variables.Replace(oldVariable, newVariable);
                }
                var newStatement = statement.WithDeclaration(statement.Declaration.WithVariables(variables));
                yield return newStatement;
            }
            else if (statement1.IsKind(SyntaxKind.ExpressionStatement))
            {
                // f(); -> Log(f(),null,...,2) or f();Log(null,null,...,3);
                var statement = statement1 as ExpressionStatementSyntax;
                var expression = statement.Expression;
                var type = SemanticModel.GetTypeInfo(expression).ConvertedType;
                if (type == null) { yield return statement; continue; }
                bool isVoid = ((object)type == Compilation.GetSpecialType(SpecialType.System_Void));
                if (isVoid)
                {
                    yield return statement;
                    var log = SyntaxFactory_Log(null, null, null, statement.GetLocation(), 3);
                    yield return SyntaxFactory.ExpressionStatement(log);
                }
                else
                {
                    var log = SyntaxFactory_Log(type, expression, null, statement.GetLocation(), 2);
                    yield return statement.WithExpression(log);
                }
            }
            else
            {
                yield return statement1;
            }
        }
    }

    ExpressionSyntax SyntaxFactory_Log(ITypeSymbol type, ExpressionSyntax expr, string id, Location loc, int reason)
    {
        // Generates "global::System.Runtime.CompilerServices.Replay.Log<type>(expr,"id","loc.FilePath",loc.Line,reason)"
        // type, expr and id may be null.

        if (type == null && expr == null) type = Compilation.GetSpecialType(SpecialType.System_Object);
        if (expr == null) expr = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);

        var typeName = SyntaxFactory.ParseTypeName(type.ToDisplayString());
        var replay = SyntaxFactory.QualifiedName(
                SyntaxFactory.QualifiedName(
                    SyntaxFactory.QualifiedName(
                        SyntaxFactory.AliasQualifiedName(
                            SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
                            SyntaxFactory.IdentifierName("System")),
                        SyntaxFactory.IdentifierName("Runtime")),
                    SyntaxFactory.IdentifierName("CompilerServices")),
                SyntaxFactory.IdentifierName("Replay"));
        var log = (type == null)
            ? SyntaxFactory.QualifiedName(replay, SyntaxFactory.IdentifierName("Log"))
            : SyntaxFactory.QualifiedName(replay, SyntaxFactory.GenericName(SyntaxFactory.Identifier("Log"),
                    SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(new[] { typeName }))));
        var args = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[] {
                        SyntaxFactory.Argument(expr),
                        SyntaxFactory.Argument(id == null ? SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression) : SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(id))),
                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(loc.SourceTree.FilePath))),
                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(loc.GetMappedLineSpan().StartLinePosition.Line))),
                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(reason)))
                    }));
        var invocation = SyntaxFactory.InvocationExpression(log, args);
        return invocation;
    }
}
