INSTRUCTIONS FOR .NET CORE COMMAND LINE
=========================================

I assumed you've installed .NETCore SDK. Patch it with the dlls...

    OSX: sudo cp *CodeAnalysis*.dll /usr/local/share/dotnet/shared/Microsoft.NETCore.App/1.0.0-rc2-3002702
    Win: copy the DLLs into C:\Program Files\dotnet\shared\Microsoft.NETCore.App\1.0.0-rc2-3002702

This sample measures perf, so you should test it in Release mode:

    dotnet restore
    dotnet run -c Release

It's possible-but-fragile to patch Omnisharp (the VS-Code plugin from https://github.com/OmniSharp/omnisharp-vscode/releases )
Success depends on whether the version of Omnisharp is close enough to the version of ArbitraryAsyncReturns. If it is,

    OSX: cp *CodeAnalysis*.dll ~/.vscode/extensions/ms-vscode.csharp-1.0.12/.omnisharp/


INSTRUCTIONS FOR VISUAL STUDIO 2015
=====================================

Run the RoslynDeployment.VSIX. This will patch your installation of VS2015 to use the arbitrary-async-returns
feature.

