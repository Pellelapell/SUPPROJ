using System.Collections.Generic;
using UnityEngine;

// Porte de la carte (prefab WoodDoors) pilotée par DoorNetwork : elle suit l'état partagé au lieu d'être
// ouverte localement. Placée et numérotée par Tools > Multijoueur > Créer le menu et la scène de jeu.
public class SyncedDoor : MonoBehaviour
{
    public int index;
    [Tooltip("Nom affiché dans l'HUD (ex : Salle 101)")]
    public string label;

    static readonly List<SyncedDoor> all = new List<SyncedDoor>();
    public static int Count
    {
        get
        {
            int count = 0;
            foreach (SyncedDoor d in all) count = Mathf.Max(count, d.index + 1);
            return count;
        }
    }

    DoorScript.Door door;
    bool synced;

    public bool IsOpen => DoorNetwork.Instance != null && DoorNetwork.Instance.IsOpen(index);

    void Awake() => door = GetComponentInChildren<DoorScript.Door>();
    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    void Update()
    {
        if (door == null || DoorNetwork.Instance == null) return;

        bool open = IsOpen;
        if (!synced)
        {
            // Premier état reçu (arrivée en cours de partie) : pas de son
            door.open = open;
            synced = true;
        }
        else if (door.open != open)
        {
            door.OpenDoor(); // bascule avec le son d'ouverture / fermeture
        }
    }

    public static SyncedDoor Nearest(Vector3 position, float maxDistance)
    {
        SyncedDoor best = null;
        float bestDistance = maxDistance;
        foreach (SyncedDoor d in all)
        {
            Vector3 p = d.transform.position;
            if (!FloorLevel.SameHeight(p.y, position.y)) continue; // pas les portes des autres étages
            float distance = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(position.x, position.z));
            if (distance < bestDistance)
            {
                best = d;
                bestDistance = distance;
            }
        }
        return best;
    }
}
