using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Partage d'écran et de caméra de l'organisateur (admin) vers les élèves assis à un bureau.
//
// Vidéo (navigateur) : WebRTC, une connexion directe organisateur -> élève, fluide et sans délai. Le jeu ne fait que
// transmettre la négociation (message Rtc, relayé par le serveur). L'image arrive directement dans une texture.
// Secours (connexion directe impossible, ou test dans l'éditeur) : l'organisateur envoie aussi des images JPEG au serveur,
// qui les relaie aux élèves assis sans WebRTC. Ceux en WebRTC ne reçoivent qu'un signal "en direct" à la place.
//
// Les textures sont affichées sur les écrans de la carte (ScreenDisplay), sur l'écran du bureau quand on est assis
// (MonitorView) ou en grand dans l'HUD (GameHud).
// Dans la scène de jeu, créé par CampusMapBuilder (ou NetworkGameManager pour une scène plus ancienne).
public class ClassStream : MonoBehaviour
{
    const string FrameMessage = "ClassStream.Frame";
    const string WatchMessage = "ClassStream.Watch";
    const string RtcMessage = "ClassStream.Rtc";
    const int MaxFrameBytes = 512 * 1024;
    // Sans nouvelle image depuis ce délai, le partage est considéré comme arrêté (organisateur parti...)
    const float LiveTimeout = 4f;
    // Secondes entre deux images JPEG de secours : écran, caméra
    static readonly float[] SendInterval = { 0.5f, 0.25f };

    // Contenu d'un message Frame
    const byte FrameStop = 0, FrameImage = 1, FrameLive = 2;
    // Ce que reçoit un élève assis
    const byte WatchNone = 0, WatchJpeg = 1, WatchRtc = 2;

    public static ClassStream Instance { get; private set; }

    public Texture2D ScreenTexture => Texture(MediaSource.Screen);
    public Texture2D CameraTexture => Texture(MediaSource.Camera);
    public bool ScreenLive => IsLive(MediaSource.Screen);
    public bool CameraLive => IsLive(MediaSource.Camera);
    public bool AnyLive => ScreenLive || CameraLive;
    // Message d'erreur de la dernière capture (ex : partage d'écran indisponible)
    public string CaptureError => capture.Error;

    readonly MediaCapture capture = new MediaCapture();
    readonly Dictionary<ulong, byte> watchers = new Dictionary<ulong, byte>(); // serveur : élèves assis
    readonly Texture2D[] jpegTextures = new Texture2D[2];
    readonly Texture2D[] videoTextures = new Texture2D[2];
    readonly bool[] videoActive = new bool[2];
    readonly float[] lastFrame = { -99f, -99f };
    readonly float[] nextSend = new float[2];
    readonly bool[] wasActive = new bool[2];
    ulong presenter;
    bool watching;
    byte sentWatch;
    bool displayedScreen, displayedCamera;
    Texture displayedScreenTexture, displayedCameraTexture;
    Material screenMaterial, cameraMaterial;

    NetworkManager Network => NetworkManager.Singleton;

    void Awake()
    {
        Instance = this;
        jpegTextures[0] = new Texture2D(2, 2, TextureFormat.RGB24, false);
        jpegTextures[1] = new Texture2D(2, 2, TextureFormat.RGB24, false);
    }

    void Start()
    {
        Material template = ScreenDisplay.Template;
        if (template != null)
        {
            screenMaterial = new Material(template);
            cameraMaterial = new Material(template);
        }

        if (Network == null || Network.CustomMessagingManager == null) return;
        Network.CustomMessagingManager.RegisterNamedMessageHandler(FrameMessage, OnFrame);
        Network.CustomMessagingManager.RegisterNamedMessageHandler(WatchMessage, OnWatch);
        Network.CustomMessagingManager.RegisterNamedMessageHandler(RtcMessage, OnRtc);
        Network.OnClientDisconnectCallback += OnClientDisconnect;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        capture.Dispose();
        WebRtc.CloseViewing();
        ScreenDisplay.Show(null, false, null, false);
        if (Network != null && Network.CustomMessagingManager != null)
        {
            Network.CustomMessagingManager.UnregisterNamedMessageHandler(FrameMessage);
            Network.CustomMessagingManager.UnregisterNamedMessageHandler(WatchMessage);
            Network.CustomMessagingManager.UnregisterNamedMessageHandler(RtcMessage);
        }
        if (Network != null) Network.OnClientDisconnectCallback -= OnClientDisconnect;
        foreach (Texture2D t in jpegTextures.Concat(videoTextures))
            if (t != null) Destroy(t);
        if (screenMaterial != null) Destroy(screenMaterial);
        if (cameraMaterial != null) Destroy(cameraMaterial);
    }

    bool IsLive(MediaSource source) => Time.unscaledTime - lastFrame[(int)source] < LiveTimeout;

    // Vidéo WebRTC si elle tourne, sinon la dernière image JPEG
    Texture2D Texture(MediaSource source) => videoActive[(int)source] ? videoTextures[(int)source] : jpegTextures[(int)source];

    // Nom de l'organisateur qui partage
    public string PresenterName
    {
        get
        {
            NetworkPlayer p = NetworkPlayer.All.FirstOrDefault(x => x != null && x.OwnerClientId == presenter);
            return p != null ? p.PlayerName.Value.ToString() : "l'organisateur";
        }
    }

    // ---------- Organisateur ----------

    public MediaCapture.State GetCaptureState(MediaSource source) => capture.GetState(source);

    public void Toggle(MediaSource source)
    {
        if (!AppRole.IsAdmin) return;
        if (capture.IsActive(source)) capture.Stop(source);
        else capture.Start(source);
    }

    void Update()
    {
        if (Network == null || !Network.IsConnectedClient && !Network.IsServer) return;

        for (int i = 0; i < 2; i++)
        {
            var source = (MediaSource)i;
            bool active = capture.IsActive(source);
            // Partage arrêté (touche, ou bandeau du navigateur) : les écrans s'éteignent tout de suite
            if (wasActive[i] && !active) Send(source, FrameStop, null);
            wasActive[i] = active;

            // Vidéo : aperçu de sa propre capture (organisateur), sinon vidéo reçue (élève assis)
            videoActive[i] = active && WebRtc.UpdateTexture(source, true, ref videoTextures[i])
                             || !active && watching && WebRtc.UpdateTexture(source, false, ref videoTextures[i]);

            if (!active || Time.unscaledTime < nextSend[i]) continue;
            byte[] jpg = capture.Grab(source);
            if (jpg == null || jpg.Length > MaxFrameBytes) continue;
            nextSend[i] = Time.unscaledTime + SendInterval[i];
            Send(source, FrameImage, jpg);
        }

        PumpSignals();
        UpdateWatchMode();
        UpdateDisplays();
    }

    void UpdateDisplays()
    {
        if (screenMaterial == null) return;
        bool screenOn = ScreenLive, cameraOn = CameraLive;
        Texture screen = ScreenTexture, camera = CameraTexture;
        if (screen != displayedScreenTexture) screenMaterial.mainTexture = displayedScreenTexture = screen;
        if (camera != displayedCameraTexture) cameraMaterial.mainTexture = displayedCameraTexture = camera;
        if (screenOn == displayedScreen && cameraOn == displayedCamera) return;
        displayedScreen = screenOn;
        displayedCamera = cameraOn;
        ScreenDisplay.Show(screenMaterial, screenOn, cameraMaterial, cameraOn);
    }

    // Image (ou arrêt) de l'organisateur local : affichée chez lui puis envoyée au serveur, ou relayée s'il est l'hôte
    void Send(MediaSource source, byte kind, byte[] jpg)
    {
        ulong self = Network.LocalClientId;
        Apply(source, self, kind, jpg);
        if (Network.IsServer)
            Forward(source, self, kind, jpg);
        else
            using (FastBufferWriter writer = WriteFrame(source, self, kind, jpg))
                Network.CustomMessagingManager.SendNamedMessage(FrameMessage, NetworkManager.ServerClientId, writer,
                    NetworkDelivery.ReliableFragmentedSequenced);
    }

    static FastBufferWriter WriteFrame(MediaSource source, ulong sender, byte kind, byte[] jpg)
    {
        int length = kind == FrameImage ? jpg.Length : 0;
        var writer = new FastBufferWriter(length + 16, Allocator.Temp);
        writer.WriteValueSafe((byte)source);
        writer.WriteValueSafe(sender);
        writer.WriteValueSafe(kind);
        writer.WriteValueSafe(length);
        if (length > 0) writer.WriteBytesSafe(jpg, length);
        return writer;
    }

    // ---------- Serveur ----------

    // Image aux élèves sans WebRTC, simple signal "en direct" aux autres (ils ont déjà la vidéo)
    void Forward(MediaSource source, ulong sender, byte kind, byte[] jpg)
    {
        var targets = watchers.Where(w => w.Key != sender && w.Key != NetworkManager.ServerClientId && Network.ConnectedClients.ContainsKey(w.Key))
            .ToList();
        SendFrame(targets.Where(w => kind != FrameImage || w.Value == WatchJpeg).Select(w => w.Key).ToList(), source, sender, kind, jpg);
        if (kind == FrameImage)
            SendFrame(targets.Where(w => w.Value == WatchRtc).Select(w => w.Key).ToList(), source, sender, FrameLive, null);
    }

    void SendFrame(List<ulong> targets, MediaSource source, ulong sender, byte kind, byte[] jpg)
    {
        if (targets.Count == 0) return;
        using (FastBufferWriter writer = WriteFrame(source, sender, kind, jpg))
            Network.CustomMessagingManager.SendNamedMessage(FrameMessage, targets, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    void OnWatch(ulong sender, FastBufferReader reader)
    {
        if (!Network.IsServer) return;
        reader.ReadValueSafe(out byte mode);
        if (mode == WatchNone) watchers.Remove(sender);
        else watchers[sender] = mode;
    }

    void OnClientDisconnect(ulong clientId)
    {
        if (Network == null || !Network.IsServer) return;
        watchers.Remove(clientId);
        Route(clientId, WebRtc.AllAdmins, "{\"type\":\"leave\"}");
    }

    // ---------- Réception ----------

    void OnFrame(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte sourceByte);
        reader.ReadValueSafe(out ulong presenterId);
        reader.ReadValueSafe(out byte kind);
        reader.ReadValueSafe(out int length);
        if (sourceByte > 1 || kind > FrameLive || length < 0 || length > MaxFrameBytes) return;
        var jpg = new byte[length];
        if (length > 0) reader.ReadBytesSafe(ref jpg, length);
        var source = (MediaSource)sourceByte;

        if (Network.IsServer)
        {
            // Seuls les admins partagent ; l'expéditeur est celui de la connexion, pas celui écrit dans le message
            if (NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsAdmin(sender)) return;
            Forward(source, sender, kind, jpg);
            if (watching) Apply(source, sender, kind, jpg);
        }
        else
        {
            Apply(source, presenterId, kind, jpg);
        }
    }

    void Apply(MediaSource source, ulong presenterId, byte kind, byte[] jpg)
    {
        int i = (int)source;
        presenter = presenterId;
        if (kind == FrameStop)
            lastFrame[i] = -99f;
        else if (kind == FrameLive || jpegTextures[i].LoadImage(jpg))
            lastFrame[i] = Time.unscaledTime;
    }

    // ---------- Joueur local assis / debout ----------

    public void SetWatching(bool on)
    {
        if (watching == on) return;
        watching = on;
        if (Network == null || !Network.IsConnectedClient && !Network.IsServer) return;
        if (on)
        {
            // Images JPEG tout de suite, vidéo directe dès que la connexion WebRTC est établie
            if (WebRtc.Supported) SendSignal(WebRtc.AllAdmins, "{\"type\":\"join\"}");
        }
        else
        {
            SendSignal(WebRtc.AllAdmins, "{\"type\":\"leave\"}");
            WebRtc.CloseViewing();
            for (int i = 0; i < 2; i++) videoActive[i] = false;
        }
        UpdateWatchMode();
    }

    void UpdateWatchMode()
    {
        byte mode = !watching ? WatchNone : WebRtc.Connected ? WatchRtc : WatchJpeg;
        if (mode == sentWatch) return;
        sentWatch = mode;
        if (Network.IsServer) return; // l'hôte affiche les images reçues directement
        using (var writer = new FastBufferWriter(1, Allocator.Temp))
        {
            writer.WriteValueSafe(mode);
            Network.CustomMessagingManager.SendNamedMessage(WatchMessage, NetworkManager.ServerClientId, writer);
        }
    }

    // ---------- Négociation WebRTC (relayée par le serveur) ----------

    // Messages préparés par le navigateur ("idDestinataire|json")
    void PumpSignals()
    {
        string signal;
        while ((signal = WebRtc.NextSignal()) != null)
        {
            int bar = signal.IndexOf('|');
            if (bar > 0 && ulong.TryParse(signal.Substring(0, bar), out ulong target))
                SendSignal(target, signal.Substring(bar + 1));
        }
    }

    void SendSignal(ulong target, string json)
    {
        if (Network.IsServer)
        {
            Route(Network.LocalClientId, target, json);
            return;
        }
        using (var writer = new FastBufferWriter(json.Length * 2 + 16, Allocator.Temp))
        {
            writer.WriteValueSafe(target);
            writer.WriteValueSafe(json);
            Network.CustomMessagingManager.SendNamedMessage(RtcMessage, NetworkManager.ServerClientId, writer,
                NetworkDelivery.ReliableFragmentedSequenced);
        }
    }

    // Client -> serveur : (destinataire, json) ; serveur -> client : (expéditeur, json)
    void OnRtc(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong id);
        reader.ReadValueSafe(out string json);
        if (Network.IsServer) Route(sender, id, json);
        else WebRtc.Receive(id, json);
    }

    // Serveur : la négociation ne se fait qu'entre un élève et un organisateur
    void Route(ulong from, ulong target, string json)
    {
        NetworkGameManager manager = NetworkGameManager.Instance;
        if (manager == null) return;
        if (target == WebRtc.AllAdmins)
        {
            foreach (ulong id in Network.ConnectedClientsIds.ToList())
                if (id != from && manager.IsAdmin(id)) Deliver(from, id, json);
        }
        else if (Network.ConnectedClientsIds.Contains(target) && (manager.IsAdmin(from) || manager.IsAdmin(target)))
        {
            Deliver(from, target, json);
        }
    }

    void Deliver(ulong from, ulong target, string json)
    {
        if (target == Network.LocalClientId)
        {
            WebRtc.Receive(from, json);
            return;
        }
        using (var writer = new FastBufferWriter(json.Length * 2 + 16, Allocator.Temp))
        {
            writer.WriteValueSafe(from);
            writer.WriteValueSafe(json);
            Network.CustomMessagingManager.SendNamedMessage(RtcMessage, target, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }
    }
}
