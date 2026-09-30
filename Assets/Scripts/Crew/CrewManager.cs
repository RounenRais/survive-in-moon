using System.Collections.Generic;
using UnityEngine;

// The base crew: the player plus the other astronauts (3 people in total).
// For now the others are picked at random from the character list (never the same as the player's character).
// They start inside the base hub, out of sight; they come out of the hub door when they get a job or an order
// (build terminal, crew window). The crew window (CrewUI) is created here too.
public class CrewManager : MonoBehaviour
{
    public static CrewManager Instance { get; private set; }

    [Tooltip("Crew size including the player")]
    [Min(1)] public int crewSize = 3;
    [Tooltip("Where the crew comes out of the hub and goes back in (facing outward)")]
    public Transform hubDoor;

    private readonly List<CrewMember> members = new List<CrewMember>();
    public IReadOnlyList<CrewMember> Members => members;
    public Transform HubDoor => hubDoor != null ? hubDoor : transform;

    void Awake() => Instance = this;

    void Start()
    {
        AddPlayer();
        SpawnOthers();
        CrewUI.Ensure();
    }

    // Position in the line of followers (0 = first), so they don't all stand on the same spot
    public int FollowerIndex(CrewMember member)
    {
        int index = 0;
        foreach (CrewMember m in members)
        {
            if (m == member) return index;
            if (m.Order == CrewOrder.Follow) index++;
        }
        return index;
    }

    void AddPlayer()
    {
        var movement = FindFirstObjectByType<PlayerMovement>();
        if (movement == null) return;
        CrewMember member = movement.GetComponent<CrewMember>();
        if (member == null) member = movement.gameObject.AddComponent<CrewMember>();
        member.isPlayer = true;
        var character = movement.GetComponent<PlayerCharacter>();
        member.Character = character != null ? character.Current : null;
        member.displayName = member.Character != null ? $"You ({member.Character.displayName})" : "You";
        members.Add(member);
    }

    void SpawnOthers()
    {
        CharacterDatabase database = CharacterDatabase.Instance;
        if (database == null) return;

        // Random order, without the player's character
        var pool = new List<CharacterDefinition>();
        foreach (CharacterDefinition c in database.characters)
            if (c != null && c.prefab != null && (members.Count == 0 || c != members[0].Character)) pool.Add(c);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int count = Mathf.Min(crewSize - members.Count, pool.Count);
        for (int i = 0; i < count; i++)
        {
            CharacterDefinition character = pool[i];
            GameObject astronaut = Instantiate(character.prefab, BaseArea.GroundPoint(HubDoor.position), HubDoor.rotation);
            astronaut.name = "Crew_" + character.id;

            // Moved by script, not by physics: a kinematic body lets its collider move along smoothly
            Rigidbody rb = astronaut.GetComponent<Rigidbody>();
            if (rb == null) rb = astronaut.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            CrewMember member = astronaut.AddComponent<CrewMember>();
            member.Character = character;
            member.displayName = character.displayName;
            members.Add(member);
            member.EnterHub(); // Out of sight until they get something to do
        }
    }
}
