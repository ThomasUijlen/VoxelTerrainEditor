using Godot;
using System;
using System.Collections.Generic;

namespace VoxelPlugin {
public partial class ThreadPool : Node
{
    private const int THREAD_COUNT = 5;
    private PoolThread[] threadPool = new PoolThread[THREAD_COUNT];
    private List<FunctionRequest> functionQueue = new List<FunctionRequest>(1000);

    private bool poolActive = true;

    public override void _Ready()
    {
        for(int i = 0; i < threadPool.Length; i++) {
            threadPool[i] = new PoolThread();
            PoolThread poolThread = threadPool[i];
            poolThread.pool = this;
            int threadI = i;
            Action action = () => {ThreadFunction(threadI);};
            poolThread.thread.Start(Callable.From(action), GodotThread.Priority.Normal);
        }
    }

    public override void _Process(double delta)
    {
        if(functionQueue.Count == 0) return;

        for(int i = 0; i < threadPool.Length; i++) {
            if(functionQueue.Count == 0) break;
            PoolThread poolThread = threadPool[i];

            if(!poolThread.thread.IsAlive()) {
                poolThread.thread.WaitToFinish();
                int threadI = i;
                Action action = () => {ThreadFunction(threadI);};
                poolThread.thread.Start(Callable.From(action), GodotThread.Priority.Normal);
            }
            
            if(!poolThread.active) {
                poolThread.CallFunction(functionQueue[0]);
                functionQueue.RemoveAt(0);
            }
        }
    }

    public bool ThreadFree() {
        for(int i = 0; i < threadPool.Length; i++) {
            if(!threadPool[i].active) return true;
        }
        return false;
    }

    public bool ThreadsActive() {
        for(int i = 0; i < threadPool.Length; i++) {
            if(threadPool[i].active) return true;
        }
        return false;
    }

    public FunctionRequest RequestFunctionCall(Node node, string functionName) {
        FunctionRequest functionRequest = new FunctionRequest(node, functionName);
        functionRequest.pool = this;
        functionQueue.Add(functionRequest);
        return functionRequest;
    }

    public FunctionRequest RequestFunctionCall(Node node, string functionName, Godot.Collections.Array parameters) {
        FunctionRequest functionRequest = new FunctionRequest(node, functionName, parameters);
        functionRequest.pool = this;
        functionQueue.Add(functionRequest);
        return functionRequest;
    }

    public void CancelRequest(FunctionRequest request) {
        if(functionQueue.Contains(request)) functionQueue.Remove(request);
    }

    private void ThreadFunction(int i) {
        PoolThread poolThread = threadPool[i];

        while(poolActive) {
            poolThread.semaphore.Wait();
            if(!poolActive) return;
            if(poolThread.functionRequest.node != null && IsInstanceValid(poolThread.functionRequest.node)) {
                if(poolThread.functionRequest.parameters != null) {
                    poolThread.functionRequest.node.Callv(
                        poolThread.functionRequest.functionName,
                        poolThread.functionRequest.parameters);
                } else {
                    poolThread.functionRequest.node.Call(
                        poolThread.functionRequest.functionName);
                }
                
            }
            poolThread.CallDeferred("FunctionFinished");
        }
    }

    public override void _ExitTree()
    {
        poolActive = false;
        for(int i = 0; i < threadPool.Length; i++) {
            PoolThread poolThread = threadPool[i];
            poolThread.semaphore.Post();
        }
    }

    private partial class PoolThread : Resource {
        public ThreadPool pool;
        public GodotThread thread;
        public Semaphore semaphore;
        public bool active;
        public FunctionRequest functionRequest;

        public PoolThread() {
            thread = new GodotThread();
            semaphore = new Semaphore();
            active = false;
        }

        public void CallFunction(FunctionRequest functionRequest) {
            this.functionRequest = functionRequest;
            functionRequest.processed = true;
            active = true;
            semaphore.Post();
        }

        public void FunctionFinished() {
            active = false;
            functionRequest = null;
        }
    }

    public class FunctionRequest {
        public Node node;
        public String functionName;
        public Godot.Collections.Array parameters;
        public bool processed = false;
        public ThreadPool pool;

        public FunctionRequest(Node node, String functionName, Godot.Collections.Array parameters) : this(node, functionName) {
            this.parameters = parameters;
        }

        public FunctionRequest(Node node, String functionName) {
            this.node = node;
            this.functionName = functionName;
        }
    }
}
}