using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebWindowCSharp;

namespace BlazorDesktop {
    public class BlazorDispatcher {

        public static BlazorDispatcher Instance {get;set;}

        public Thread CreationThread {get;}

        public BlazorDispatcher() {
            CreationThread = Thread.CurrentThread;
        }

        public void 
        Invoke(Action action){
            if (Thread.CurrentThread == CreationThread)
                action();
            else 
                WebWindow.Invoke(action);
        }

        public void 
        VerifyAccess([CallerMemberName] string? caller = null){
            if (Thread.CurrentThread != CreationThread)
                throw new InvalidOperationException($"Called {caller} in a non UI thread.");
        }

        public void Run(){
            BlazorWindowHelper.WaitForExit();
        }
    }
}
