using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace RadialConvert
{
    // Reads which files are selected in the Explorer window (or desktop) under the cursor.
    // Must run on an STA thread (the UI thread).
    static class ShellSelection
    {
        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface IShellWindows
        {
            [PreserveSig] int get_Count(out int count);
            [PreserveSig] int Item(object index, [MarshalAs(UnmanagedType.IDispatch)] out object folder);
            [PreserveSig] int _NewEnum([MarshalAs(UnmanagedType.IUnknown)] out object ppunk);
            void Register();
            void RegisterPending();
            void Revoke();
            void OnNavigate();
            void OnActivated();
            [PreserveSig] int FindWindowSW(ref object pvarLoc, ref object pvarLocRoot, int swClass, out int phwnd, int swfwOptions, [MarshalAs(UnmanagedType.IDispatch)] out object ppdispOut);
        }

        [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IComServiceProvider
        {
            [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellBrowser
        {
            [PreserveSig] int GetWindow(out IntPtr hwnd);
        }

        static readonly Guid CLSID_ShellWindows = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        static readonly Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
        const int SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1;

        public static List<string> At(Point pt)
        {
            var files = new List<string>();
            bool desktop;
            IntPtr root = Native.ShellRootAt(pt, out desktop);
            if (root == IntPtr.Zero) return files;
            try
            {
                object view = desktop ? DesktopView() : ExplorerView(root);
                if (view != null) Collect(view, files);
            }
            catch (Exception ex)
            {
                Log.Write("selection lookup failed: " + ex.Message);
            }
            return files;
        }

        static void Collect(object browser, List<string> files)
        {
            dynamic b = browser;
            dynamic items = b.Document.SelectedItems();
            int n = items.Count;
            for (int i = 0; i < n; i++)
            {
                dynamic it = items.Item(i);
                if (it == null) continue;
                string p = it.Path;
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) files.Add(p);
            }
        }

        static IShellWindows ShellWindows()
        {
            return (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ShellWindows));
        }

        static object DesktopView()
        {
            var sw = ShellWindows();
            object loc = 0;     // CSIDL_DESKTOP
            object locRoot = null;
            int hwnd;
            object disp;
            sw.FindWindowSW(ref loc, ref locRoot, SWC_DESKTOP, out hwnd, SWFO_NEEDDISPATCH, out disp);
            return disp;
        }

        static object ExplorerView(IntPtr root)
        {
            // Windows 11 tabs: every tab reports the same top window; the active tab's
            // ShellTabWindowClass child is first in z-order.
            IntPtr activeTab = Native.FindWindowEx(root, IntPtr.Zero, "ShellTabWindowClass", null);
            var sw = ShellWindows();
            int count;
            sw.get_Count(out count);
            object first = null;
            for (int i = 0; i < count; i++)
            {
                object w;
                if (sw.Item(i, out w) != 0 || w == null) continue;
                long h;
                try { h = Convert.ToInt64(((dynamic)w).HWND); } catch { continue; }
                if (h != root.ToInt64()) continue;
                if (activeTab == IntPtr.Zero) return w;
                if (first == null) first = w;
                if (BrowserWindow(w) == activeTab) return w;
            }
            return first;
        }

        static IntPtr BrowserWindow(object w)
        {
            try
            {
                var sp = w as IComServiceProvider;
                if (sp == null) return IntPtr.Zero;
                Guid sid = SID_STopLevelBrowser, iid = IID_IShellBrowser;
                object o;
                if (sp.QueryService(ref sid, ref iid, out o) != 0 || o == null) return IntPtr.Zero;
                var sb = o as IShellBrowser;
                IntPtr h;
                if (sb != null && sb.GetWindow(out h) == 0) return h;
            }
            catch { }
            return IntPtr.Zero;
        }
    }
}
