using System.Collections.Generic;
using UnityEngine;

// Chaise de bureau où un joueur peut s'asseoir (F). Placée, numérotée et tournée vers son bureau par CampusMapBuilder.
// Qui est assis où : NetworkPlayer.SeatIndex, partagé par le réseau.
public class Seat : MonoBehaviour
{
    public int index;
    [Tooltip("Point d'assise, au sol")]
    public Vector3 point;
    [Tooltip("Direction du regard de la personne assise (vers le bureau)")]
    public float yaw;
    [Tooltip("Hauteur de l'assise au-dessus du sol")]
    public float seatHeight = 0.48f;
    [Tooltip("Hauteur du plateau du bureau (0 : pas de bureau devant)")]
    public float deskHeight = 0.75f;
    [Tooltip("Distance entre le point d'assise et le bord du bureau")]
    public float deskDistance = 0.35f;
    [Tooltip("Écran du PC sur le bureau : la caméra zoome dessus quand on s'assoit (vide : pas d'écran)")]
    public ScreenDisplay monitor;

    static readonly List<Seat> all = new List<Seat>();

    void OnEnable()
    {
        all.Add(this);
        // Les écrans des bureaux restent éteints sauf pour celui qui est assis devant
        if (monitor != null) monitor.desk = true;
    }
    void OnDisable() => all.Remove(this);

    public Quaternion Rotation => Quaternion.Euler(0f, yaw, 0f);
    // En se levant, on recule derrière la chaise
    public Vector3 StandPoint => point - Rotation * Vector3.forward * 0.75f;

    public bool IsTaken(NetworkPlayer except)
    {
        foreach (NetworkPlayer p in NetworkPlayer.All)
            if (p != except && p.SeatIndex.Value == index) return true;
        return false;
    }

    public static Seat Get(int index)
    {
        foreach (Seat s in all)
            if (s.index == index) return s;
        return null;
    }

    public static Seat NearestFree(Vector3 position, float maxDistance, NetworkPlayer self)
    {
        Seat best = null;
        float bestDistance = maxDistance;
        foreach (Seat s in all)
        {
            if (!FloorLevel.SameHeight(s.point.y, position.y)) continue; // pas les chaises des autres étages
            float distance = Vector2.Distance(new Vector2(s.point.x, s.point.z), new Vector2(position.x, position.z));
            if (distance < bestDistance && !s.IsTaken(self))
            {
                best = s;
                bestDistance = distance;
            }
        }
        return best;
    }
}
