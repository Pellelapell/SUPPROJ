using System;
using System.Runtime.InteropServices;
using UnityEngine;

public enum MediaSource { Screen = 0, Camera = 1 }

// Capture de l'écran et de la caméra de l'organisateur, en images JPEG prêtes à envoyer (ClassStream).
//   Jeu (navigateur, WebGL) : getDisplayMedia / getUserMedia du navigateur, voir Plugins/WebGL/MediaCapture.jslib
//   Éditeur, pour tester avec Play : caméra via WebCamTexture, écran principal via GDI (éditeur Windows seulement)
public class MediaCapture : IDisposable
{
    public enum State { Off, Starting, Live, Failed }

    // Taille et qualité des images envoyées : à chaque image, l'hôte en envoie une copie à chaque élève assis
    public const int ScreenWidth = 960, CameraWidth = 320;
    const int ScreenQuality = 55, CameraQuality = 60;
    const int MaxFrameBytes = 512 * 1024;

    readonly State[] states = new State[2];
    public string Error { get; private set; }

    public State GetState(MediaSource source)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (states[(int)source] != State.Off) states[(int)source] = (State)MediaCapture_State((int)source);
#else
        if (source == MediaSource.Camera && states[1] == State.Starting && webcam != null && webcam.width > 16)
            states[1] = State.Live;
#endif
        return states[(int)source];
    }

    public bool IsActive(MediaSource source) => GetState(source) == State.Starting || GetState(source) == State.Live;

    public void Start(MediaSource source)
    {
        Error = null;
        states[(int)source] = State.Starting;
#if UNITY_WEBGL && !UNITY_EDITOR
        MediaCapture_Start((int)source);
#else
        if (source == MediaSource.Camera)
        {
            if (WebCamTexture.devices.Length == 0)
            {
                Fail(source, "Aucune caméra détectée.");
                return;
            }
            webcam = new WebCamTexture(640, 480, 15);
            webcam.Play();
        }
        else if (!DesktopCapture.Supported)
        {
            Fail(source, "Dans l'éditeur, le partage d'écran ne marche que sous Windows.");
            return;
        }
        else
        {
            states[0] = State.Live;
        }
#endif
    }

    public void Stop(MediaSource source)
    {
        states[(int)source] = State.Off;
#if UNITY_WEBGL && !UNITY_EDITOR
        MediaCapture_Stop((int)source);
#else
        if (source == MediaSource.Camera && webcam != null)
        {
            webcam.Stop();
            UnityEngine.Object.Destroy(webcam);
            webcam = null;
        }
#endif
    }

    public void Dispose()
    {
        Stop(MediaSource.Screen);
        Stop(MediaSource.Camera);
        if (readback != null) UnityEngine.Object.Destroy(readback);
    }

    void Fail(MediaSource source, string message)
    {
        states[(int)source] = State.Failed;
        Error = message;
    }

    // Image JPEG actuelle, ou null si rien de prêt
    public byte[] Grab(MediaSource source)
    {
        if (GetState(source) != State.Live) return null;
        int width = source == MediaSource.Screen ? ScreenWidth : CameraWidth;
        int quality = source == MediaSource.Screen ? ScreenQuality : CameraQuality;
#if UNITY_WEBGL && !UNITY_EDITOR
        int length = MediaCapture_Grab((int)source, width, quality / 100f, buffer, buffer.Length);
        if (length <= 0) return null;
        var jpg = new byte[length];
        Array.Copy(buffer, jpg, length);
        return jpg;
#else
        if (source == MediaSource.Screen)
        {
            Texture2D shot = DesktopCapture.Capture(width, ref readback);
            return shot != null ? shot.EncodeToJPG(quality) : null;
        }
        if (webcam == null || webcam.width <= 16) return null;
        int height = width * webcam.height / webcam.width;
        RenderTexture rt = RenderTexture.GetTemporary(width, height);
        Graphics.Blit(webcam, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        if (readback == null || readback.width != width || readback.height != height || readback.format != TextureFormat.RGB24)
        {
            if (readback != null) UnityEngine.Object.Destroy(readback);
            readback = new Texture2D(width, height, TextureFormat.RGB24, false);
        }
        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return readback.EncodeToJPG(quality);
#endif
    }

    Texture2D readback;

#if UNITY_WEBGL && !UNITY_EDITOR
    readonly byte[] buffer = new byte[MaxFrameBytes];

    [DllImport("__Internal")] static extern void MediaCapture_Start(int source);
    [DllImport("__Internal")] static extern void MediaCapture_Stop(int source);
    [DllImport("__Internal")] static extern int MediaCapture_State(int source);
    [DllImport("__Internal")] static extern int MediaCapture_Grab(int source, int maxWidth, float quality, byte[] buffer, int size);
#else
    WebCamTexture webcam;
#endif
}

// Éditeur Windows : capture de l'écran principal, réduite à la volée par GDI (StretchBlt)
static class DesktopCapture
{
#if UNITY_EDITOR_WIN
    public static bool Supported => true;

    public static Texture2D Capture(int width, ref Texture2D texture)
    {
        int screenWidth = GetSystemMetrics(0), screenHeight = GetSystemMetrics(1);
        if (screenWidth <= 0 || screenHeight <= 0) return null;
        width = Mathf.Min(width, screenWidth);
        int height = width * screenHeight / screenWidth;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(screenDc);
        IntPtr bitmap = CreateCompatibleBitmap(screenDc, width, height);
        IntPtr old = SelectObject(memoryDc, bitmap);
        var pixels = new byte[width * height * 4];
        try
        {
            SetStretchBltMode(memoryDc, Halftone);
            StretchBlt(memoryDc, 0, 0, width, height, screenDc, 0, 0, screenWidth, screenHeight, SrcCopy | CaptureBlt);
            SelectObject(memoryDc, old);
            // Hauteur positive : lignes du bas vers le haut, le même sens que les textures Unity
            var header = new BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = width,
                biHeight = height,
                biPlanes = 1,
                biBitCount = 32,
            };
            if (GetDIBits(memoryDc, bitmap, 0, (uint)height, pixels, ref header, 0) == 0) return null;
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        if (texture == null || texture.width != width || texture.height != height || texture.format != TextureFormat.BGRA32)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = new Texture2D(width, height, TextureFormat.BGRA32, false);
        }
        texture.LoadRawTextureData(pixels);
        texture.Apply(false);
        return texture;
    }

    const int Halftone = 4;
    const int SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hDc);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hDc, int mode);
    [DllImport("gdi32.dll")]
    static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, int srcWidth, int srcHeight, int rop);
    [DllImport("gdi32.dll")]
    static extern int GetDIBits(IntPtr hDc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader header, uint usage);
#else
    public static bool Supported => false;
    public static Texture2D Capture(int width, ref Texture2D texture) => null;
#endif
}

// Diffusion WebRTC et vidéo copiée directement dans une texture (navigateur seulement, voir MediaCapture.jslib).
// Ailleurs (éditeur), tout répond "indisponible" et ClassStream utilise l'envoi d'images JPEG.
static class WebRtc
{
    public const ulong AllAdmins = ulong.MaxValue;

#if UNITY_WEBGL && !UNITY_EDITOR
    public static bool Supported => Rtc_Supported() == 1;
    public static bool Connected => Rtc_Connected() == 1;
    public static string NextSignal() => Rtc_NextSignal();
    public static void Receive(ulong from, string json) => Rtc_Receive(from.ToString(), json);
    public static void CloseViewing() => Rtc_CloseViewing();

    // Image actuelle de la vidéo (locale : capture de l'organisateur ; sinon : reçue) dans texture, recréée à sa taille
    public static bool UpdateTexture(MediaSource source, bool local, ref Texture2D texture)
    {
        int size = Media_VideoSize((int)source, local ? 1 : 0);
        if (size <= 0) return false;
        int width = size / 65536, height = size % 65536;
        if (texture == null || texture.width != width || texture.height != height)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.Apply(false, true); // crée la texture côté GPU, sans copie en mémoire
        }
        return Media_UpdateTexture((int)source, local ? 1 : 0, (int)texture.GetNativeTexturePtr(), width, height) == 1;
    }

    [DllImport("__Internal")] static extern int Rtc_Supported();
    [DllImport("__Internal")] static extern int Rtc_Connected();
    [DllImport("__Internal")] static extern string Rtc_NextSignal();
    [DllImport("__Internal")] static extern void Rtc_Receive(string from, string json);
    [DllImport("__Internal")] static extern void Rtc_CloseViewing();
    [DllImport("__Internal")] static extern int Media_VideoSize(int source, int local);
    [DllImport("__Internal")] static extern int Media_UpdateTexture(int source, int local, int texture, int width, int height);
#else
    public static bool Supported => false;
    public static bool Connected => false;
    public static string NextSignal() => null;
    public static void Receive(ulong from, string json) { }
    public static void CloseViewing() { }
    public static bool UpdateTexture(MediaSource source, bool local, ref Texture2D texture) => false;
#endif
}
