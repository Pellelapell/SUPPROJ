// Rôle de l'exécutable, fixé à la compilation par Tools > Build :
// le build Admin (symbole ADMIN_BUILD) ouvre et héberge les rooms, le build Élève peut seulement les rejoindre.
// Dans l'éditeur on est admin, sauf si "Tools > Build > Tester en élève dans l'éditeur" est coché.
public static class AppRole
{
    public const string EditorStudentKey = "SUPPROJ.TestAsStudent";

    public static bool IsAdmin
    {
        get
        {
#if ADMIN_BUILD
            return true;
#elif UNITY_EDITOR
            return !UnityEditor.EditorPrefs.GetBool(EditorStudentKey, false);
#else
            return false;
#endif
        }
    }
}
