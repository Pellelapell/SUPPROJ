using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Petit kit d'UI généré par code (couleurs, coins arrondis, boutons, champs) pour le menu principal.
// Tout est créé à l'exécution : pas de sprite ni de police à importer.
public static class MenuStyle
{
    public static readonly Color BackgroundTop = new Color32(0x13, 0x1A, 0x30, 0xFF);
    public static readonly Color BackgroundBottom = new Color32(0x05, 0x07, 0x10, 0xFF);
    public static readonly Color Accent = new Color32(0x1C, 0x3F, 0xAA, 0xFF);
    public static readonly Color AccentHover = new Color32(0x2A, 0x56, 0xD8, 0xFF);
    public static readonly Color Secondary = new Color32(0x4C, 0xE0, 0xD2, 0xFF);
    public static readonly Color Panel = new Color(1f, 1f, 1f, 0.06f);
    public static readonly Color PanelBorder = new Color(1f, 1f, 1f, 0.12f);
    public static readonly Color Text = new Color32(0xF4, 0xF1, 0xFF, 0xFF);
    public static readonly Color TextDim = new Color(0.96f, 0.95f, 1f, 0.55f);

    static Font font;
    static Sprite rounded, roundedSmall;

    public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    // Rectangle aux coins arrondis, étirable (9-slice)
    public static Sprite Rounded => rounded != null ? rounded : rounded = MakeRounded(64, 20);
    public static Sprite RoundedSmall => roundedSmall != null ? roundedSmall : roundedSmall = MakeRounded(32, 10);

    static Sprite MakeRounded(int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Distance au coin arrondi le plus proche, avec 1 px d'anticrénelage
            float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
            float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
            byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255);
            pixels[y * size + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    static Texture2D radialGlow;

    // Halo circulaire blanc qui s'estompe vers les bords (teinté par la couleur de l'image)
    public static Texture2D RadialGlow
    {
        get
        {
            if (radialGlow != null) return radialGlow;
            const int size = 128;
            radialGlow = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                radialGlow.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
            radialGlow.Apply();
            return radialGlow;
        }
    }

    // Dégradé vertical (haut -> bas)
    public static Texture2D Gradient(Color top, Color bottom)
    {
        var tex = new Texture2D(1, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 256; y++)
            tex.SetPixel(0, y, Color.Lerp(bottom, top, y / 255f));
        tex.Apply();
        return tex;
    }

    // ---------- Fabriques ----------

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5; // UI
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static RectTransform Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        return rt;
    }

    public static Image Box(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        img.sprite = sprite != null ? sprite : Rounded;
        img.type = Image.Type.Sliced;
        img.color = color;
        return img;
    }

    public static Text Label(string name, Transform parent, string text, int size, Color color,
        TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
    {
        var t = Rect(name, parent).gameObject.AddComponent<Text>();
        t.font = Font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    public static Button Button(string name, Transform parent, string text, int size, Color color, Color hover, Color textColor,
        UnityAction onClick)
    {
        Image img = Box(name, parent, Color.white);
        var button = img.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        var colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = hover;
        colors.selectedColor = color;
        colors.pressedColor = Color.Lerp(color, Color.black, 0.2f);
        colors.disabledColor = new Color(color.r, color.g, color.b, color.a * 0.35f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(onClick);
        img.gameObject.AddComponent<HoverScale>();

        Text label = Label("Text", img.transform, text, size, textColor, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(label.rectTransform);
        return button;
    }

    public static InputField Input(string name, Transform parent, string value, int size, int maxLength)
    {
        Image bg = Box(name, parent, new Color(1f, 1f, 1f, 0.08f));
        var field = bg.gameObject.AddComponent<InputField>();
        field.targetGraphic = bg;

        Text text = Label("Text", bg.transform, value, size, Text);
        text.supportRichText = false;
        text.raycastTarget = true;
        Stretch(text.rectTransform, 22, 22, 0, 0);

        field.textComponent = text;
        field.characterLimit = maxLength;
        field.text = value;
        field.caretColor = Accent;
        field.caretWidth = 3;
        field.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
        field.customCaretColor = true;
        var colors = field.colors;
        colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
        field.colors = colors;
        return field;
    }

    public static LayoutElement Size(Component c, float height = -1, float width = -1, float flexibleWidth = -1)
    {
        var le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        if (height >= 0) le.preferredHeight = le.minHeight = height;
        if (width >= 0) le.preferredWidth = le.minWidth = width;
        if (flexibleWidth >= 0) le.flexibleWidth = flexibleWidth;
        return le;
    }

    public static VerticalLayoutGroup Column(Component c, float spacing, RectOffset padding = null)
    {
        var g = c.gameObject.AddComponent<VerticalLayoutGroup>();
        g.spacing = spacing;
        g.padding = padding ?? new RectOffset();
        g.childControlWidth = g.childControlHeight = true;
        g.childForceExpandWidth = true;
        g.childForceExpandHeight = false;
        return g;
    }

    public static HorizontalLayoutGroup Row(Component c, float spacing, RectOffset padding = null)
    {
        var g = c.gameObject.AddComponent<HorizontalLayoutGroup>();
        g.spacing = spacing;
        g.padding = padding ?? new RectOffset();
        g.childControlWidth = g.childControlHeight = true;
        g.childForceExpandWidth = false;
        g.childForceExpandHeight = true;
        g.childAlignment = TextAnchor.MiddleLeft;
        return g;
    }
}

// Léger grossissement au survol d'un bouton
public class HoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public float hoverScale = 1.04f;
    bool hovered;

    public void OnPointerEnter(PointerEventData e) => hovered = true;
    public void OnPointerExit(PointerEventData e) => hovered = false;

    void OnDisable() => hovered = false;

    void Update()
    {
        var selectable = GetComponent<Selectable>();
        float target = hovered && (selectable == null || selectable.interactable) ? hoverScale : 1f;
        float s = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
        transform.localScale = new Vector3(s, s, 1f);
    }
}

// Zone qui transmet le glisser horizontal de la souris (pour faire tourner l'aperçu du perso)
public class DragArea : MonoBehaviour, IDragHandler
{
    public Action<float> onDrag;
    public void OnDrag(PointerEventData e) => onDrag?.Invoke(e.delta.x);
}
