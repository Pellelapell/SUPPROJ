using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Chat écrit en jeu (bas gauche). Entrée : écrire / envoyer, Échap : annuler.
// Les messages passent par le NetworkPlayer local (RPC), qui les renvoie à tout le monde.
// Hors saisie, les messages s'effacent au bout de quelques secondes.
public class GameChat : MonoBehaviour
{
    public const int MaxLength = 120;
    const int MaxLines = 8;
    const float LineLifetime = 12f;

    public static GameChat Instance { get; private set; }
    public static bool IsTyping => Instance != null && Instance.typing;
    // Frame où la saisie a été fermée par Échap : le menu pause ne doit pas s'ouvrir sur la même touche
    public static int ClosedFrame { get; private set; } = -1;

    struct Line { public Text text; public float time; }

    readonly List<Line> lines = new List<Line>();
    RectTransform linesRoot;
    Image background;
    InputField field;
    GameObject inputRow, hint;
    bool typing;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        MenuStyle.EnsureEventSystem();
        Transform canvas = MenuStyle.OverlayCanvas("ChatCanvas", 10);

        background = MenuStyle.Box("Chat", canvas, new Color(0.02f, 0.03f, 0.08f, 0.6f));
        MenuStyle.Place(background, Vector2.zero, new Vector2(30f, 30f), new Vector2(560f, 340f));
        MenuStyle.Column(background, 8f, new RectOffset(18, 18, 14, 14)).childAlignment = TextAnchor.LowerLeft;

        linesRoot = MenuStyle.Rect("Lines", background.transform);
        MenuStyle.Column(linesRoot, 4f).childAlignment = TextAnchor.LowerLeft;
        MenuStyle.Size(linesRoot, -1, -1, 1f).flexibleHeight = 1f;

        field = MenuStyle.Input("Input", background.transform, "", 20, MaxLength);
        MenuStyle.Size(field, 46f);
        inputRow = field.gameObject;

        Text hintText = MenuStyle.WithShadow(MenuStyle.Label("Hint", background.transform, "ENTRÉE  écrire dans le chat", 16, MenuStyle.TextDim));
        MenuStyle.Size(hintText, 22f);
        hint = hintText.gameObject;

        SetTyping(false);
    }

    void Update()
    {
        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (!typing && enter && !GameHud.Blocking)
            SetTyping(true);
        else if (typing && enter)
            Send();
        else if (typing && Input.GetKeyDown(KeyCode.Escape))
        {
            ClosedFrame = Time.frameCount;
            SetTyping(false);
        }

        // Les lignes s'effacent hors saisie
        foreach (Line line in lines)
        {
            float age = Time.unscaledTime - line.time;
            float alpha = typing ? 1f : Mathf.Clamp01((LineLifetime - age) / 1.5f);
            var c = line.text.color;
            line.text.color = new Color(c.r, c.g, c.b, alpha);
        }
    }

    void SetTyping(bool value)
    {
        typing = value;
        field.text = "";
        inputRow.SetActive(value);
        hint.SetActive(!value);
        background.color = new Color(0.02f, 0.03f, 0.08f, value ? 0.75f : 0f);
        if (value)
        {
            field.Select();
            field.ActivateInputField();
        }
    }

    public void AddMessage(string author, string message)
    {
        string color = ColorUtility.ToHtmlStringRGB(MenuStyle.Secondary);
        AddLine($"<color=#{color}><b>{Escape(author)}</b></color>  {Escape(message)}", MenuStyle.Text);
    }

    // Message du jeu (arrivées, départs...)
    public void AddSystem(string message) => AddLine($"<i>{Escape(message)}</i>", MenuStyle.TextDim);

    void AddLine(string richText, Color color)
    {
        if (linesRoot == null) return;
        Text text = MenuStyle.WithShadow(MenuStyle.Label("Line", linesRoot, richText, 20, color));
        text.supportRichText = true;
        lines.Add(new Line { text = text, time = Time.unscaledTime });
        if (lines.Count > MaxLines)
        {
            Destroy(lines[0].text.gameObject);
            lines.RemoveAt(0);
        }
    }

    // Empêche les joueurs d'injecter des balises de texte riche (<size=200>...)
    static string Escape(string text) => text.Replace("<", "‹").Replace(">", "›");

    void Send()
    {
        string text = field.text.Trim();
        SetTyping(false);
        if (text.Length == 0) return;

        var localPlayer = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
        if (localPlayer != null)
            localPlayer.GetComponent<NetworkPlayer>().SendChat(text);
    }
}
