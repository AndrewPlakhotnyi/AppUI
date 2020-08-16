using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorDesktop {
public class 
DesktopSynchronizationContext: SynchronizationContext {
    public event EventHandler<Exception>? UnhandledException;
    private CancellationToken _cancellationToken {get;}
    private Thread _queueThread {get;}
    private BlockingCollection<WorkItem> _queue { get; } = new BlockingCollection<WorkItem>();

    public DesktopSynchronizationContext(CancellationToken cancellationToken) {
        _cancellationToken = cancellationToken;
        _queueThread = new Thread(ProcessQueue);
        _queueThread.Start();
    } 

    public override void 
    Post(SendOrPostCallback callback, object state) =>
        _queue.Add(new WorkItem(
            callback: callback,
            context:this,
            state: state, 
            completed:null), _cancellationToken);

    public override void 
    Send(SendOrPostCallback callback, object state) {
        if (CheckAccess())
            try {
                callback(state);
            } catch(Exception exception) {
                UnhandledException?.Invoke(this, exception);
            }
        else {
            var completed = new ManualResetEventSlim();
            _queue.Add(new WorkItem(
                callback:callback,
                context:this,
                state: state,
                completed: completed), _cancellationToken);
            completed.Wait(_cancellationToken);
        }
    }

    private void 
    ProcessQueue() {
        SetSynchronizationContext(this);
        while(!_queue.IsCompleted) {
            WorkItem item;
            try {
                item = _queue.Take(_cancellationToken);
            }
            catch(InvalidCastException) {
              return;
            }
            catch(OperationCanceledException) {
              return;
            }

            try {
                item.Callback(item.State);
            }
            catch(Exception exception) {
                UnhandledException?.Invoke(this, exception);
            }
            finally {
                item.Completed?.Set();
            }
        }
    }
    
    public override SynchronizationContext 
    CreateCopy() => this;

    public void 
    Stop() => _queue.CompleteAdding();

    public static bool 
    CheckAccess() {
        if (!(Current is DesktopSynchronizationContext synchronizationContext) || synchronizationContext == null)
            return false;

        if (synchronizationContext._queueThread != Thread.CurrentThread)
            return false;

        return true;
    }

    public static void 
    VerifyAccess() {
        if (!CheckAccess())
            throw new InvalidOperationException("Not in the right context");
    }
    private class 
    WorkItem {
        public WorkItem(SendOrPostCallback callback, object state, SynchronizationContext context, ManualResetEventSlim? completed) {
            Callback = callback;
            State = state;
            Context = context;
            Completed = completed;
        }
        public SendOrPostCallback Callback {get;}
        public object State {get;}
        public SynchronizationContext Context {get;}
        public ManualResetEventSlim? Completed {get;}
    }

}
}
