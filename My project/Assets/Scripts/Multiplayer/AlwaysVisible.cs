using UnityEngine;

// Marque un groupe d'objets affiché sur plusieurs étages (un escalier, vu des deux étages qu'il relie), quel que soit
// l'étage de la racine FloorLevel qui le contient. Voir FloorLevel.
public class AlwaysVisible : MonoBehaviour
{
    [Tooltip("Affiché quand le joueur local est à un étage entre minLevel et maxLevel (inclus)")]
    public int minLevel = 0, maxLevel = 99;

    public bool VisibleOn(int level) => level >= minLevel && level <= maxLevel;
}
