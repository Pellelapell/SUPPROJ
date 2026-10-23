using Unity.Netcode.Components;

// NetworkAnimator piloté par le propriétaire : paramètres et triggers envoyés par le joueur qui contrôle le perso.
public class OwnerNetworkAnimator : NetworkAnimator
{
    protected override bool OnIsServerAuthoritative() => false;
}
