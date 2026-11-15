using System.Collections.Generic;
using UnityEngine;

// What happened during this mission, for the debrief (DebriefUI). Reset when a new game starts.
// Mission time: 1 real minute = 1 mission day.
// Radiation is tracked in real units: the lunar surface dose measured by LRO/CRaTER (NASA RadLab) turned into an
// effective dose (mSv), so it can be compared with NASA's astronaut career limit.
public static class MissionLog
{
    public const float SecondsPerDay = 60f;
    public const float CareerLimitMsv = 600f; // NASA's career effective dose limit for astronauts (2022 standard)
    const float QualityFactor = 2.5f;          // Average for galactic cosmic rays: Gy -> Sv

    public static float DoseMsv;
    public static float SecondsPlayed;
    public static float PowerOutSeconds;
    public static int CropsHarvested;
    public static int MealsGivenToCrew;
    public static float CrewHungrySeconds;
    public static int Deaths;
    public static readonly List<string> Storms = new List<string>(); // "M2.1 · 2026-10-12"

    public static float Days => SecondsPlayed / SecondsPerDay;
    public static float PowerUptime => SecondsPlayed > 0f ? 1f - PowerOutSeconds / SecondsPlayed : 1f;

    // The everyday lunar surface dose per mission day in mSv (median of the real measurements)
    public static float DailyMsv
    {
        get
        {
            float uGyPerHour = NasaData.HasRadiation ? NasaData.Info.moonDoseMedian : 13.2f;
            return uGyPerHour * 24f / 1000f * QualityFactor;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        DoseMsv = SecondsPlayed = PowerOutSeconds = CrewHungrySeconds = 0f;
        CropsHarvested = MealsGivenToCrew = Deaths = 0;
        Storms.Clear();
    }

    // Called every frame by SurvivalStats. exposure: 1 = standing on the open surface on a calm day
    public static void Tick(float dt, float exposure)
    {
        SecondsPlayed += dt;
        DoseMsv += DailyMsv * exposure * dt / SecondsPerDay;
        if (BaseSupplies.Instance != null && !BaseSupplies.Instance.HasPower) PowerOutSeconds += dt;
    }
}
