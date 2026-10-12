using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>Editor switch for the difficulty (PlayerPrefs "oz_difficulty"), same values as the menu: Easy / Medium / Hard.</summary>
    public static class OpenZDifficultyMenu
    {
        const string E = "OpenZ/Difficulty/Easy", M = "OpenZ/Difficulty/Medium", H = "OpenZ/Difficulty/Hard (exactly Z)";

        static void Set(GameDifficulty d) { OpenZDifficulty.Level = d; Debug.Log($"OpenZ difficulty: {OpenZDifficulty.Label(d)} ({OpenZDifficulty.Hint(d)})"); }
        [MenuItem(E)] static void SetEasy() => Set(GameDifficulty.Easy);
        [MenuItem(M)] static void SetMedium() => Set(GameDifficulty.Medium);
        [MenuItem(H)] static void SetHard() => Set(GameDifficulty.Hard);
        [MenuItem(E, true)] static bool CheckEasy() { Menu.SetChecked(E, OpenZDifficulty.Level == GameDifficulty.Easy); return true; }
        [MenuItem(M, true)] static bool CheckMedium() { Menu.SetChecked(M, OpenZDifficulty.Level == GameDifficulty.Medium); return true; }
        [MenuItem(H, true)] static bool CheckHard() { Menu.SetChecked(H, OpenZDifficulty.Level == GameDifficulty.Hard); return true; }
    }
}
