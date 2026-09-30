using UnityEngine;

// Remembers which character the player picked, even after the game is closed (PlayerPrefs).
// A character select screen only needs:   CharacterSelection.Select(character);
// The player in the game scene reads:      CharacterSelection.Current
public static class CharacterSelection
{
    const string SaveKey = "SurviveInMoon.SelectedCharacter";

    // Fires when the choice changes, so a player that is already in the scene can switch right away
    public static event System.Action<CharacterDefinition> Changed;

    public static CharacterDefinition Current
    {
        get
        {
            CharacterDatabase database = CharacterDatabase.Instance;
            if (database == null) return null;
            CharacterDefinition saved = database.Get(PlayerPrefs.GetString(SaveKey, ""));
            return saved != null ? saved : database.Default;
        }
    }

    public static void Select(CharacterDefinition character)
    {
        if (character == null) return;
        PlayerPrefs.SetString(SaveKey, character.id);
        PlayerPrefs.Save();
        Changed?.Invoke(character);
    }

    public static void Select(string id)
    {
        CharacterDatabase database = CharacterDatabase.Instance;
        if (database != null) Select(database.Get(id));
    }
}
