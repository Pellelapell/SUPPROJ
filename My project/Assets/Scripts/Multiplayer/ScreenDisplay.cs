using System.Collections.Generic;
using UnityEngine;

// Écran de la carte (moniteur d'un bureau, tableau, grand écran de l'amphi) qui affiche le partage de l'organisateur.
// Placé par CampusMapBuilder ; ClassStream lui donne les images. Sans partage, l'image est masquée (écran éteint).
public class ScreenDisplay : MonoBehaviour
{
    [Tooltip("Image principale : l'écran partagé, ou la caméra si l'écran n'est pas partagé")]
    public Renderer main;
    [Tooltip("Incrustation de la caméra quand l'écran et la caméra sont partagés tous les deux (optionnelle)")]
    public Renderer inset;
    [Tooltip("Moniteur d'un bureau : éteint, sauf celui du joueur assis (évite d'afficher la vidéo des dizaines de fois)")]
    public bool desk;

    static readonly List<ScreenDisplay> all = new List<ScreenDisplay>();
    static Material screenMaterial, cameraMaterial;
    static bool screenLive, cameraLive;
    static ScreenDisplay active;

    void OnEnable()
    {
        all.Add(this);
        Refresh();
    }

    void OnDisable() => all.Remove(this);

    // Matériau d'origine des écrans (Unlit/Texture, créé par CampusMapBuilder), pour en faire les copies affichées
    public static Material Template
    {
        get
        {
            foreach (ScreenDisplay d in all)
                if (d.main != null) return d.main.sharedMaterial;
            return null;
        }
    }

    public static void Show(Material screenMaterial, bool screenOn, Material cameraMaterial, bool cameraOn)
    {
        ScreenDisplay.screenMaterial = screenMaterial;
        ScreenDisplay.cameraMaterial = cameraMaterial;
        screenLive = screenOn && ScreenDisplay.screenMaterial != null;
        cameraLive = cameraOn && ScreenDisplay.cameraMaterial != null;
        foreach (ScreenDisplay d in all) d.Refresh();
    }

    // Moniteur du bureau où le joueur local est assis (null : debout)
    public static void SetActive(ScreenDisplay display)
    {
        if (display == active) return;
        ScreenDisplay previous = active;
        active = display;
        if (previous != null) previous.Refresh();
        if (active != null) active.Refresh();
    }

    void Refresh()
    {
        bool on = !desk || this == active;
        if (main != null)
        {
            main.enabled = on && (screenLive || cameraLive);
            if (main.enabled) main.sharedMaterial = screenLive ? screenMaterial : cameraMaterial;
        }
        if (inset != null)
        {
            inset.enabled = on && screenLive && cameraLive;
            if (inset.enabled) inset.sharedMaterial = cameraMaterial;
        }
    }
}
