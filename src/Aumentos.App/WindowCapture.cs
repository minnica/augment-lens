using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Aumentos;

internal record GameWindow(nint Handle, Int32Rect Bounds, Int32Rect Frame, string? Patch)
{
    public static GameWindow? Foreground(GameWindow? previous = null)
    {
        var handle = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(handle, out var id);
        if (id == 0)
            return null;
        try
        {
            using var process = Process.GetProcessById((int)id);
            if (process.ProcessName != "League of Legends")
                return null;
            string? version = previous?.Handle == handle ? previous.Patch : null;
            if (version is null)
            {
                try
                {
                    var file = process.MainModule?.FileName;
                    if (file is not null)
                        version = LeaguePatch.ReadBinary(file);
                }
                catch (System.ComponentModel.Win32Exception) { }
            }
            var frame = Native.Bounds(handle);
            return new GameWindow(handle, Native.ClientBounds(handle, frame), frame, version ?? LeaguePatch.Installed());
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}

internal static class LeaguePatch
{
    // Riot labels the public patch by season year, while the client reports its binary major version.
    internal static string Public(string clientPatch)
    {
        var version = Version.Parse(clientPatch);
        return $"{version.Major + 10}.{version.Minor}";
    }

    internal static string? Previous(string patch)
    {
        var version = Version.Parse(patch);
        return version.Minor > 1 ? $"{version.Major}.{version.Minor - 1}" : null;
    }

    internal static string? Installed()
    {
        var metadata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Riot Games", "Metadata", "league_of_legends.live", "league_of_legends.live.product_settings.yaml");
        try
        {
            var install = FindInstallPath(File.ReadLines(metadata));
            return install is null ? null : ReadBinary(Path.Combine(install, "Game", "League of Legends.exe"));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal static string? FindInstallPath(System.Collections.Generic.IEnumerable<string> metadata)
    {
        foreach (var line in metadata)
        {
            var entry = Regex.Match(line, "^product_install_full_path:[ \\t]*[\\\"']?(?<path>.*?)[\\\"']?[ \\t]*$");
            if (entry.Success && Path.IsPathFullyQualified(entry.Groups["path"].Value))
                return entry.Groups["path"].Value;
        }
        return null;
    }

    internal static string? ReadBinary(string path)
    {
        var version = FileVersionInfo.GetVersionInfo(path);
        return version.FileMajorPart > 0 ? $"{version.FileMajorPart}.{version.FileMinorPart}" : null;
    }
}

internal sealed unsafe class WindowCapture : IDisposable
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private ID3D11Texture2D? staging;
    private Int32Rect stagingRegion;
    private bool disposed;
    public nint Handle { get; }
    public int Width { get; }
    public int Height { get; }

    public WindowCapture(nint handle)
    {
        Handle = handle;
        if (!GraphicsCaptureSession.IsSupported())
            throw new InvalidOperationException("Windows Graphics Capture no está disponible.");
        var item = CreateItem(handle);
        Width = item.Size.Width;
        Height = item.Size.Height;
        if (Width < 1 || Height < 1)
            throw new InvalidOperationException("La ventana no tiene un área visible.");

        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [], out device, out context).CheckError();
        Direct3D11CaptureFramePool? newPool = null;
        GraphicsCaptureSession? newSession = null;
        try
        {
            using var dxgi = device.QueryInterface<IDXGIDevice>();
            Marshal.ThrowExceptionForHR(Native.CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var pointer));
            IDirect3DDevice bridge;
            try { bridge = MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
            finally { Marshal.Release(pointer); }
            newPool = Direct3D11CaptureFramePool.CreateFreeThreaded(bridge,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            newSession = newPool.CreateCaptureSession(item);
            newSession.IsCursorCaptureEnabled = false;
            newSession.StartCapture();
            pool = newPool;
            session = newSession;
        }
        catch
        {
            newSession?.Dispose();
            newPool?.Dispose();
            context.Dispose();
            device.Dispose();
            throw;
        }
    }

    public BitmapSource? Read(Int32Rect region)
    {
        using var frame = pool.TryGetNextFrame();
        if (frame is null || frame.ContentSize.Width != Width || frame.ContentSize.Height != Height)
            return null;
        if (region.Width < 1 || region.Height < 1 || region.X < 0 || region.Y < 0 ||
            region.X + region.Width > Width || region.Y + region.Height > Height)
            return null;

        if (staging is null || stagingRegion.Width != region.Width || stagingRegion.Height != region.Height)
        {
            staging?.Dispose();
            staging = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)region.Width, (uint)region.Height, 1, 1, BindFlags.None,
                ResourceUsage.Staging, CpuAccessFlags.Read));
            stagingRegion = region;
        }

        using var source = GetTexture(frame.Surface);
        var box = new Box(region.X, region.Y, 0, region.X + region.Width, region.Y + region.Height, 1);
        context.CopySubresourceRegion(staging, 0, 0, 0, 0, source, 0, box);
        var mapped = context.Map(staging, 0, MapMode.Read);
        try
        {
            var stride = region.Width * 4;
            var pixels = new byte[stride * region.Height];
            for (int line = 0; line < region.Height; line++)
                Marshal.Copy(mapped.DataPointer + line * (int)mapped.RowPitch, pixels, line * stride, stride);
            var bitmap = BitmapSource.Create(region.Width, region.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
            bitmap.Freeze();
            return bitmap;
        }
        finally { context.Unmap(staging, 0); }
    }

    private static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        using var surfaceReference = MarshalInterface<IDirect3DSurface>.CreateMarshaler(surface);
        var accessId = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfaceReference.ThisPtr, in accessId, out var access));
        try
        {
            var textureId = typeof(ID3D11Texture2D).GUID;
            var method = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)access)[3];
            nint texture;
            Marshal.ThrowExceptionForHR(method(access, &textureId, &texture));
            return new ID3D11Texture2D(texture);
        }
        finally { Marshal.Release(access); }
    }

    private static GraphicsCaptureItem CreateItem(nint handle)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(Native.WindowsCreateString(className, className.Length, out var classId));
        try
        {
            var interopId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(Native.RoGetActivationFactory(classId, in interopId, out var factory));
            try
            {
                var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                var method = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[3];
                nint item;
                Marshal.ThrowExceptionForHR(method(factory, handle, &itemId, &item));
                try { return GraphicsCaptureItem.FromAbi(item); }
                finally { Marshal.Release(item); }
            }
            finally { Marshal.Release(factory); }
        }
        finally { Native.WindowsDeleteString(classId); }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        session.Dispose();
        pool.Dispose();
        staging?.Dispose();
        context.Dispose();
        device.Dispose();
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint handle);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint handle, out uint process);
    [DllImport("user32.dll")] internal static extern int GetWindowLongW(nint handle, int index);
    [DllImport("user32.dll")] internal static extern int SetWindowLongW(nint handle, int index, int value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint handle, nint after,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, int attribute,
        out Rect bounds, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint handle, ref Point location);
    [DllImport("d3d11.dll")] internal static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint bridge);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] internal static extern int WindowsCreateString(
        string text, int length, out nint result);
    [DllImport("combase.dll")] internal static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] internal static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);

    internal static Int32Rect Bounds(nint handle)
    {
        if (DwmGetWindowAttribute(handle, 9, out var bounds, Marshal.SizeOf<Rect>()) != 0)
            GetWindowRect(handle, out bounds);
        return new Int32Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    internal static Int32Rect ClientBounds(nint handle, Int32Rect frame)
    {
        Point origin = default;
        if (!GetClientRect(handle, out var bounds) || !ClientToScreen(handle, ref origin))
            return frame;
        var left = Math.Max(frame.X, origin.X);
        var top = Math.Max(frame.Y, origin.Y);
        var right = Math.Min(frame.X + frame.Width, origin.X + bounds.Right);
        var bottom = Math.Min(frame.Y + frame.Height, origin.Y + bounds.Bottom);
        return right > left && bottom > top ? new Int32Rect(left, top, right - left, bottom - top) : frame;
    }
}
