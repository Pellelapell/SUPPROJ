using System.Collections.Generic;
using UnityEngine;

// Étage du campus (rez-de-chaussée, étage 1) dans la scène de jeu : seul l'étage où se trouve le joueur local est affiché
// (en vue du dessus, l'étage 1 cacherait le rez-de-chaussée), ainsi que les autres joueurs de cet étage.
// Les collisions restent actives partout. Les objets sous un AlwaysVisible (l'escalier) restent toujours affichés.
// Placé par CampusMapBuilder sur la racine de chaque étage.
public class FloorLevel : MonoBehaviour
{
    [Tooltip("Numéro de l'étage (0 : rez-de-chaussée)")]
    public int level;
    [Tooltip("Altitude du sol de l'étage")]
    public float baseHeight;

    // On est à un étage dès qu'on est à moins de cette hauteur sous son sol (en haut de l'escalier)
    const float Threshold = 1f;

    public static int Current { get; private set; }

    static readonly List<FloorLevel> all = new List<FloorLevel>();
    static int updatedFrame = -1;
    // Rendus des autres joueurs et leur état affiché, pour ne toucher qu'aux joueurs qui changent d'étage
    static readonly Dictionary<NetworkPlayer, (Renderer[] renderers, bool visible)> players =
        new Dictionary<NetworkPlayer, (Renderer[], bool)>();
    static readonly List<NetworkPlayer> gone = new List<NetworkPlayer>();

    Renderer[] renderers;
    bool shown = true;

    void Awake()
    {
        var list = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r.GetComponentInParent<AlwaysVisible>() == null) list.Add(r);
        renderers = list.ToArray();
    }

    void OnEnable() => all.Add(this);

    void OnDisable()
    {
        all.Remove(this);
        Show(true);
    }

    void LateUpdate()
    {
        if (updatedFrame != Time.frameCount)
        {
            updatedFrame = Time.frameCount;
            Transform local = CameraFollow.Instance != null ? CameraFollow.Instance.target : null;
            Current = local != null ? LevelAt(local.position.y) : 0;
            HideOtherPlayers(local);
        }
        Show(level == Current);
    }

    // Étage le plus haut dont le sol est sous (ou juste au-dessus de) cette altitude
    public static int LevelAt(float y)
    {
        int best = 0;
        float bestHeight = float.MinValue;
        foreach (FloorLevel f in all)
            if (y >= f.baseHeight - Threshold && f.baseHeight > bestHeight)
            {
                best = f.level;
                bestHeight = f.baseHeight;
            }
        return best;
    }

    // Est-ce que ce joueur est au même étage que le joueur local ? (pseudos, rendu)
    public static bool SameFloor(Transform player) => LevelAt(player.position.y) == Current;

    static void HideOtherPlayers(Transform local)
    {
        foreach (NetworkPlayer p in NetworkPlayer.All)
        {
            if (p == null || p.transform == local) continue;
            bool visible = SameFloor(p.transform);
            if (players.TryGetValue(p, out var state) && state.visible == visible) continue;
            Renderer[] renderers = state.renderers ?? p.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
                if (r != null) r.forceRenderingOff = !visible;
            players[p] = (renderers, visible);
        }

        // Joueurs partis
        foreach (NetworkPlayer p in players.Keys)
            if (p == null) gone.Add(p);
        foreach (NetworkPlayer p in gone) players.Remove(p);
        gone.Clear();
    }

    // forceRenderingOff : indépendant de Renderer.enabled, que d'autres scripts utilisent (plafond, écrans)
    void Show(bool on)
    {
        if (on == shown) return;
        shown = on;
        foreach (Renderer r in renderers)
            if (r != null) r.forceRenderingOff = !on;
    }
}
