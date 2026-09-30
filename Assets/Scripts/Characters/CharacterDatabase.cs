using System.Collections.Generic;
using UnityEngine;

// The list of all playable characters. There is one of these, at Assets/Resources/CharacterDatabase.asset,
// so any script (the player, a character select screen) can load it with CharacterDatabase.Instance.
// Adding a character = create a CharacterDefinition and drag it into the Characters list.
[CreateAssetMenu(menuName = "Survive In Moon/Character Database", fileName = "CharacterDatabase")]
public class CharacterDatabase : ScriptableObject
{
    public const string ResourcePath = "CharacterDatabase"; // Assets/Resources/CharacterDatabase.asset

    public List<CharacterDefinition> characters = new List<CharacterDefinition>();
    [Tooltip("Used when nothing was picked yet (first start of the game)")]
    public CharacterDefinition defaultCharacter;

    private static CharacterDatabase instance;
    public static CharacterDatabase Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<CharacterDatabase>(ResourcePath);
            return instance;
        }
    }

    public CharacterDefinition Get(string id)
    {
        foreach (CharacterDefinition c in characters)
            if (c != null && c.id == id) return c;
        return null;
    }

    public CharacterDefinition Default =>
        defaultCharacter != null ? defaultCharacter : characters.Find(c => c != null);
}
