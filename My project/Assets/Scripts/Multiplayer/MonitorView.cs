using UnityEngine;
using UnityEngine.UI;

// Interface du poste de l'élève assis, dessinée sur l'écran 3D de son bureau (canvas dans le monde) :
// barre du haut, partage de l'organisateur (écran + caméra en incrustation) ou écran d'attente.
// S'allume comme un vieil écran (une ligne lumineuse qui s'ouvre) une fois la caméra arrivée, s'éteint quand on se lève.
// Piloté par GameHud.
public class MonitorView : MonoBehaviour
{
    const float CanvasWidth = 1280f, CanvasHeight = 720f;
    const float PowerOnDelay = 0.35f; // le temps que la caméra arrive devant l'écran
    const float PowerTime = 0.4f;

    static MonitorView current;

    ScreenDisplay display;
    RectTransform content;
    Image glow;
    Text title;
    GameObject waiting;
    RawImage main, inset;
    AspectRatioFitter mainAspect, insetAspect;
    Texture2D wallpaper;
    float power, delay = PowerOnDelay;
    bool closing;

    public static void Show(ScreenDisplay display)
    {
        if (current != null && current.display == display) return;
        Hide();
        current = new GameObject("MonitorView", typeof(RectTransform)).AddComponent<MonitorView>();
        current.Build(display);
    }

    public static void Hide()
    {
        if (current == null) return;
        current.closing = true;
        current = null;
    }

    void Build(ScreenDisplay screen)
    {
        display = screen;
        // Juste devant l'image du moniteur, à sa taille
        Transform image = screen.main.transform;
        transform.SetPositionAndRotation(image.position - image.forward * 0.004f, image.rotation);
        transform.localScale = Vector3.one * (image.lossyScale.x / CanvasWidth);
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f; // texte net de près
        ((RectTransform)transform).sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

        // Dalle noire de l'écran éteint, et le contenu qui s'ouvre par-dessus
        Image panel = MenuStyle.Box("Panel", transform, Color.black);
        panel.sprite = null;
        MenuStyle.Stretch(panel.rectTransform);
        content = MenuStyle.Stretch(MenuStyle.Rect("Content", panel.transform));

        wallpaper = MenuStyle.Gradient(MenuStyle.BackgroundTop, MenuStyle.BackgroundBottom);
        RawImage background = MenuStyle.Rect("Wallpaper", content).gameObject.AddComponent<RawImage>();
        background.texture = wallpaper;
        MenuStyle.Stretch(background.rectTransform);

        Image bar = MenuStyle.Box("Bar", content, new Color(0f, 0f, 0f, 0.45f));
        bar.sprite = null;
        bar.rectTransform.anchorMin = new Vector2(0f, 1f);
        bar.rectTransform.anchorMax = Vector2.one;
        bar.rectTransform.pivot = new Vector2(0.5f, 1f);
        bar.rectTransform.sizeDelta = new Vector2(0f, 56f);
        MenuStyle.Row(bar, 12f, new RectOffset(22, 22, 0, 0)).childForceExpandHeight = false;
        MenuStyle.Size(MenuStyle.Box("Dot", bar.transform, new Color(0.95f, 0.25f, 0.3f), MenuStyle.RoundedSmall), 14f, 14f);
        title = MenuStyle.Label("Title", bar.transform, "", 26, MenuStyle.Text, TextAnchor.MiddleLeft, FontStyle.Bold);

        RectTransform area = MenuStyle.Stretch(MenuStyle.Rect("Area", content), 24f, 24f, 76f, 24f);
        area.gameObject.AddComponent<RectMask2D>();
        main = GameHud.Picture("Main", area, out mainAspect);
        MenuStyle.Stretch(main.rectTransform);
        mainAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        inset = GameHud.Picture("Camera", area, out insetAspect);
        MenuStyle.Place(inset, new Vector2(1f, 0f), new Vector2(-12f, 12f), new Vector2(300f, 225f));
        insetAspect.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;

        RectTransform wait = MenuStyle.Stretch(MenuStyle.Rect("Waiting", area));
        MenuStyle.Column(wait, 8f).childAlignment = TextAnchor.MiddleCenter;
        MenuStyle.Label("Logo", wait, "SUPINFO", 110, MenuStyle.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
        MenuStyle.Label("Text", wait, "En attente de l'organisateur…", 30, MenuStyle.TextDim, TextAnchor.MiddleCenter);
        waiting = wait.gameObject;

        // Lueur blanche de la mise sous tension
        glow = MenuStyle.Box("Glow", panel.transform, Color.white);
        glow.sprite = null;
        MenuStyle.Stretch(glow.rectTransform);

        Animate();
        Refresh();
    }

    void OnDestroy()
    {
        if (wallpaper != null) Destroy(wallpaper);
    }

    void Update()
    {
        if (display == null)
        {
            Destroy(gameObject);
            return;
        }
        if (!closing && delay > 0f)
        {
            delay -= Time.deltaTime;
            return;
        }
        power = Mathf.MoveTowards(power, closing ? 0f : 1f, Time.deltaTime / PowerTime);
        if (closing && power <= 0f)
        {
            Destroy(gameObject);
            return;
        }
        Animate();
        Refresh();
    }

    // Ligne horizontale qui s'ouvre en hauteur, puis la lueur blanche s'estompe sur l'image
    void Animate()
    {
        float open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.55f, power));
        content.localScale = new Vector3(Mathf.Lerp(0.85f, 1f, open), Mathf.Max(0.004f, open), 1f);
        content.gameObject.SetActive(power > 0f);
        glow.rectTransform.localScale = content.localScale;
        glow.color = new Color(1f, 1f, 1f, power > 0f ? 0.9f * (1f - Mathf.InverseLerp(0.35f, 1f, power)) : 0f);
    }

    void Refresh()
    {
        ClassStream stream = ClassStream.Instance;
        bool screen = stream != null && stream.ScreenLive, camera = stream != null && stream.CameraLive;
        title.text = screen || camera ? $"EN DIRECT  ·  {stream.PresenterName.ToUpperInvariant()}" : "POSTE ÉLÈVE  ·  SUPINFO";
        if (stream != null)
        {
            // Seulement la caméra : elle prend toute la place
            GameHud.ShowPicture(main, mainAspect, screen ? stream.ScreenTexture : stream.CameraTexture, screen || camera);
            GameHud.ShowPicture(inset, insetAspect, stream.CameraTexture, screen && camera);
        }
        waiting.SetActive(!screen && !camera);
    }
}
