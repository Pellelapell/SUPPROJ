using Unity.Netcode.Components;

// NetworkTransform piloté par le propriétaire : chaque joueur déplace son perso localement, sans attendre le serveur.
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
