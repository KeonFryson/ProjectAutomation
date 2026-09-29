using System;
using UnityEngine;

/// <summary>
/// Tracks the player's money. Attach to a single empty GameObject named "EconomyManager".
/// </summary>
public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance { get; private set; }

    [Tooltip("Money the player starts the game with.")]
    public int startingMoney = 100;

    public int Money { get; private set; }

    public event Action<int> OnMoneyChanged;

    void Awake()
    {
        Instance = this;
        Money = startingMoney;
    }

    void Start()
    {
        // Fire once so any UI listening late still gets the initial value.
        OnMoneyChanged?.Invoke(Money);
    }

    public void AddMoney(int amount)
    {
        if (amount == 0) return;
        Money += amount;
        OnMoneyChanged?.Invoke(Money);
    }

    public bool TrySpend(int amount)
    {
        if (amount < 0 || Money < amount) return false;
        Money -= amount;
        OnMoneyChanged?.Invoke(Money);
        return true;
    }
}
