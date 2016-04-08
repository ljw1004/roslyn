﻿using System.Threading.Tasks;
using System.Runtime.CompilerServices;

[Tasklike(typeof(IAsyncActionBuilder))]
interface IAsyncAction { }


namespace System.Runtime.CompilerServices
{
    class IAsyncActionBuilder
    {

        private AsyncTaskMethodBuilder _taskBuilder;
        private IAsyncAction _task;

        public static IAsyncActionBuilder Create() => new IAsyncActionBuilder() { _taskBuilder = AsyncTaskMethodBuilder.Create() };
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => _taskBuilder.Start(ref stateMachine);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => _taskBuilder.SetStateMachine(stateMachine);
        public void SetResult() => _taskBuilder.SetResult();
        public void SetException(Exception exception) => _taskBuilder.SetException(exception);
        public IAsyncAction Task => (_task == null) ? _task = _taskBuilder.Task.AsAsyncAction() as IAsyncAction : _task; // won't work because it's the wrong IAsyncAction :)
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine => _taskBuilder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine => _taskBuilder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }
}