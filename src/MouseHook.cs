using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RadialConvert
{
    // Global low-level mouse hook on its own thread, so slow work on the UI thread never lags the mouse.
    // Shift + right-button-down over Explorer or the desktop is held back while the UI thread looks up
    // the selection. If nothing convertible is selected, the click is replayed so the normal
    // Shift + right-click menu still opens.
    class MouseHook
    {
        enum State { Idle, Pending, Active, SwallowLeftUp }

        static readonly IntPtr Marker = new IntPtr(0x52434E56); // tags clicks we replay ourselves

        readonly Control ui;
        readonly Action<Point> onTrigger;
        readonly Action onRelease;
        readonly Action onCancel;
        readonly object sync = new object();
        State state = State.Idle;
        bool upSeen;
        int pendingSince;
        Thread thread;
        uint threadId;
        IntPtr hook;
        Native.HookProc proc;
        public volatile bool Paused;

        public MouseHook(Control ui, Action<Point> onTrigger, Action onRelease, Action onCancel)
        {
            this.ui = ui;
            this.onTrigger = onTrigger;
            this.onRelease = onRelease;
            this.onCancel = onCancel;
        }

        public void Start()
        {
            var ready = new ManualResetEvent(false);
            thread = new Thread(() =>
            {
                threadId = Native.GetCurrentThreadId();
                proc = Callback;
                hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
                ready.Set();
                Native.MSG msg;
                while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
                Native.UnhookWindowsHookEx(hook);
            });
            thread.IsBackground = true;
            thread.Name = "mouse hook";
            thread.Start();
            ready.WaitOne(3000);
            if (hook == IntPtr.Zero) Log.Write("mouse hook failed to install, error " + Marshal.GetLastWin32Error());
        }

        public bool Installed { get { return hook != IntPtr.Zero; } }

        public void Stop()
        {
            if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        // Called by the UI thread once the selection is known. Returns true if the button was
        // already released (a fast flick), in which case the caller commits straight away.
        public bool ResolvePending(bool show)
        {
            bool replayDown = false, replayUp = false, released = false;
            lock (sync)
            {
                if (state != State.Pending) return false;
                if (show)
                {
                    released = upSeen;
                    state = upSeen ? State.Idle : State.Active;
                }
                else
                {
                    replayDown = true;
                    replayUp = upSeen;
                    state = State.Idle;
                }
            }
            if (replayDown) Replay(replayUp);
            return released;
        }

        public void ForceIdle()
        {
            lock (sync) { if (state == State.Active) state = State.Idle; }
        }

        static void Replay(bool alsoUp)
        {
            var list = new System.Collections.Generic.List<Native.INPUT>();
            var down = new Native.INPUT();
            down.type = 0;
            down.mi.dwFlags = Native.MOUSEEVENTF_RIGHTDOWN;
            down.mi.dwExtraInfo = Marker;
            list.Add(down);
            if (alsoUp)
            {
                var up = down;
                up.mi.dwFlags = Native.MOUSEEVENTF_RIGHTUP;
                list.Add(up);
            }
            Native.SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf(typeof(Native.INPUT)));
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                if (info.dwExtraInfo != Marker && Handle(wParam.ToInt32(), new Point(info.pt.X, info.pt.Y)))
                    return new IntPtr(1);
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        // Returns true to swallow the event. Must stay fast: Windows drops hooks that take too long.
        bool Handle(int msg, Point pt)
        {
            lock (sync)
            {
                switch (msg)
                {
                    case Native.WM_RBUTTONDOWN:
                        // Safety valve: never stay stuck if the UI thread didn't answer.
                        if (state == State.Pending && Environment.TickCount - pendingSince > 3000) state = State.Idle;
                        if (state != State.Idle || Paused) return false;
                        if (!Native.KeyDown(Native.VK_SHIFT) || Native.KeyDown(Native.VK_CONTROL) || Native.KeyDown(Native.VK_MENU)) return false;
                        bool desk;
                        if (Native.ShellRootAt(pt, out desk) == IntPtr.Zero) return false;
                        state = State.Pending;
                        pendingSince = Environment.TickCount;
                        upSeen = false;
                        ui.BeginInvoke(onTrigger, pt);
                        return true;

                    case Native.WM_RBUTTONUP:
                        if (state == State.Pending) { upSeen = true; return true; }
                        if (state == State.Active) { state = State.Idle; ui.BeginInvoke(onRelease); return true; }
                        return false;

                    case Native.WM_LBUTTONDOWN:
                        if (state == State.Active) { state = State.SwallowLeftUp; ui.BeginInvoke(onCancel); return true; }
                        return false;

                    case Native.WM_LBUTTONUP:
                        if (state == State.SwallowLeftUp) { state = State.Idle; return true; }
                        return false;
                }
                return false;
            }
        }
    }
}
