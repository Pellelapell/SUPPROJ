using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Chat écrit en jeu (bas gauche). Entrée : écrire / envoyer, Échap : annuler.
// Les messages passent par le NetworkPlayer local (RPC), qui les renvoie à tout le monde.
public class GameChat : MonoBehaviour
{
    public const int MaxLength = 120;
    const int MaxLines = 8;
    const string InputControl = "chatInput";

    public static GameChat Instance { get; private set; }
    public static bool IsTyping => Instance != null && Instance.typing;

    readonly List<string> lines = new List<string>();
    bool typing;
    bool focusPending;
    string input = "";
    GUIStyle lineStyle;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    public void AddMessage(string author, string message)
    {
        lines.Add($"<b>{Escape(author)}</b> : {Escape(message)}");
        if (lines.Count > MaxLines) lines.RemoveAt(0);
    }

    // Empêche les joueurs d'injecter des balises de texte riche (<size=200>...)
    static string Escape(string text) => text.Replace("<", "‹").Replace(">", "›");

    void Send()
    {
        string text = input.Trim();
        input = "";
        typing = false;
        if (text.Length == 0) return;

        var localPlayer = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
        if (localPlayer != null)
            localPlayer.GetComponent<NetworkPlayer>().SendChat(text);
    }

    void OnGUI()
    {
        if (lineStyle == null)
            lineStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14, wordWrap = true };

        Event e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
        {
            if (typing) Send();
            else { typing = true; focusPending = true; }
            e.Use();
        }
        else if (typing && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            typing = false;
            input = "";
            e.Use();
        }

        float width = Mathf.Min(420f, Screen.width - 20f);
        GUILayout.BeginArea(new Rect(10f, Screen.height - 230f, width, 220f));
        GUILayout.FlexibleSpace();
        foreach (string line in lines)
            GUILayout.Label(line, lineStyle);

        if (typing)
        {
            GUI.SetNextControlName(InputControl);
            input = GUILayout.TextField(input, MaxLength, GUILayout.Height(26f));
            if (focusPending)
            {
                GUI.FocusControl(InputControl);
                focusPending = false;
            }
        }
        else
        {
            GUILayout.Label("<i>Entrée pour écrire</i>", lineStyle);
        }
        GUILayout.EndArea();
    }
}
