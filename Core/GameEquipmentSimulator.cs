namespace XunxianDpkViewer.Core;

public sealed class GameEquipmentSimulationSlot
{
    internal GameEquipmentSimulationSlot(
        GameEquipmentWashSlot definition,
        GameEquipmentWashCandidate candidate,
        int tierIndex)
    {
        Definition = definition;
        Candidate = candidate;
        TierIndex = Math.Clamp(tierIndex, 0, Math.Max(0, candidate.TierTexts.Count - 1));
    }

    public GameEquipmentWashSlot Definition { get; }
    public GameEquipmentWashCandidate Candidate { get; private set; }
    public int TierIndex { get; private set; }
    public bool IsMaxed => TierIndex >= Math.Max(0, Candidate.TierTexts.Count - 1);
    public string Text => Candidate.TierTexts.ElementAtOrDefault(TierIndex) ?? Candidate.Name;

    internal void Replace(GameEquipmentWashCandidate candidate, int tierIndex)
    {
        Candidate = candidate;
        TierIndex = Math.Clamp(tierIndex, 0, Math.Max(0, candidate.TierTexts.Count - 1));
    }

    internal bool Increase(int amount)
    {
        int next = Math.Clamp(TierIndex + Math.Max(0, amount), 0, Math.Max(0, Candidate.TierTexts.Count - 1));
        bool changed = next != TierIndex;
        TierIndex = next;
        return changed;
    }
}

public sealed record GameEquipmentWashOutcome(
    int SlotIndex,
    GameEquipmentWashCandidate Candidate,
    int TierIndex)
{
    public string Text => Candidate.TierTexts.ElementAtOrDefault(TierIndex) ?? Candidate.Name;
}

public sealed record GameEquipmentEnhancementOutcome(
    GameEquipmentEnhancementEvent Event,
    bool PerfectHit,
    IReadOnlyList<int> SelectedSlotIndexes,
    IReadOnlyList<int> ChangedSlotIndexes)
{
    public bool ConsumesMaterial => Event.ConsumesMaterial;
}

public sealed class GameEquipmentSimulator
{
    private const int ProbabilityScale = 10000;
    private readonly Random _random;
    private readonly List<GameEquipmentSimulationSlot> _slots;

    public GameEquipmentSimulator(GameEquipmentWashRecord wash, Random? random = null)
    {
        Wash = wash;
        _random = random ?? new Random();
        _slots = wash.Slots
            .OrderBy(slot => slot.Index)
            .Where(slot => slot.Candidates.Count > 0)
            .Select(CreateInitialSlot)
            .ToList();
    }

    public GameEquipmentWashRecord Wash { get; }
    public IReadOnlyList<GameEquipmentSimulationSlot> Slots => _slots;
    public GameEquipmentWashOutcome? PendingWash { get; private set; }

    public string FormatSlotText(GameEquipmentSimulationSlot slot) =>
        FormatTierText(slot.Candidate, slot.TierIndex, Wash.MaxTierMarker);

    public string FormatOutcomeText(GameEquipmentWashOutcome outcome) =>
        FormatTierText(outcome.Candidate, outcome.TierIndex, Wash.MaxTierMarker);

    public static string FormatTierText(
        GameEquipmentWashCandidate candidate,
        int tierIndex,
        string maxTierMarker)
    {
        int normalizedTier = Math.Clamp(tierIndex, 0, Math.Max(0, candidate.TierTexts.Count - 1));
        string text = candidate.TierTexts.ElementAtOrDefault(normalizedTier) ?? candidate.Name;
        bool reachedEnhancementMaximum = candidate.TierTexts.Count > 1 &&
                                         normalizedTier == candidate.TierTexts.Count - 1;
        return reachedEnhancementMaximum && !string.IsNullOrWhiteSpace(maxTierMarker)
            ? $"{text}{maxTierMarker}"
            : text;
    }

    public GameEquipmentWashOutcome? WashSlot(int slotIndex)
    {
        GameEquipmentSimulationSlot? slot = _slots.FirstOrDefault(value => value.Definition.Index == slotIndex);
        if (slot is null || !slot.Definition.IsWashable || slot.Definition.Candidates.Count == 0)
        {
            return null;
        }

        GameEquipmentWashCandidate candidate = PickWeighted(
            slot.Definition.Candidates,
            value => value.Weight);
        int tierIndex = PickWeightedIndex(candidate.InitialTierWeights, candidate.TierTexts.Count);
        PendingWash = new GameEquipmentWashOutcome(slotIndex, candidate, tierIndex);
        return PendingWash;
    }

    public bool ApplyPendingWash()
    {
        if (PendingWash is not { } pending)
        {
            return false;
        }

        GameEquipmentSimulationSlot? slot = _slots.FirstOrDefault(value =>
            value.Definition.Index == pending.SlotIndex);
        if (slot is null)
        {
            PendingWash = null;
            return false;
        }

        slot.Replace(pending.Candidate, pending.TierIndex);
        PendingWash = null;
        return true;
    }

    public bool ReplaceSlot(int slotIndex, string candidateId, int tierIndex)
    {
        GameEquipmentSimulationSlot? slot = _slots.FirstOrDefault(value =>
            value.Definition.Index == slotIndex);
        GameEquipmentWashCandidate? candidate = slot?.Definition.Candidates.FirstOrDefault(value =>
            value.Id.Equals(candidateId, StringComparison.OrdinalIgnoreCase));
        if (slot is null || !slot.Definition.IsWashable || candidate is null)
        {
            return false;
        }

        slot.Replace(candidate, tierIndex);
        PendingWash = null;
        return true;
    }

    public void DiscardPendingWash() => PendingWash = null;

    public GameEquipmentEnhancementOutcome? Enhance(
        GameEquipmentEnhancementInfo enhancement,
        int fireId,
        bool perfectHit)
    {
        if (_slots.Count == 0 || enhancement.Events.Count == 0)
        {
            return null;
        }

        GameEquipmentEnhancementEvent gameEvent = PickEnhancementEvent(enhancement, fireId, perfectHit);
        List<GameEquipmentSimulationSlot> candidates = gameEvent.OnlyUnfilled
            ? _slots.Where(slot => !slot.IsMaxed).ToList()
            : _slots.ToList();
        if (candidates.Count == 0)
        {
            candidates = _slots.ToList();
        }

        int take = Math.Min(Math.Max(1, gameEvent.AttributeCount), candidates.Count);
        var selected = new List<int>(take);
        var changed = new List<int>(take);
        for (int index = 0; index < take; index++)
        {
            int selectedIndex = _random.Next(candidates.Count);
            GameEquipmentSimulationSlot slot = candidates[selectedIndex];
            candidates.RemoveAt(selectedIndex);
            selected.Add(slot.Definition.Index);
            if (slot.Increase(gameEvent.TierIncrease))
            {
                changed.Add(slot.Definition.Index);
            }
        }

        return new GameEquipmentEnhancementOutcome(gameEvent, perfectHit, selected, changed);
    }

    internal static IReadOnlyDictionary<string, int> GetEnhancementWeights(
        GameEquipmentEnhancementInfo enhancement,
        int fireId,
        bool perfectHit)
    {
        var weights = enhancement.Events.ToDictionary(
            gameEvent => gameEvent.Id,
            gameEvent => gameEvent.Id.Equals(enhancement.BaseEventId, StringComparison.OrdinalIgnoreCase)
                ? 0
                : Math.Max(0, gameEvent.Probability),
            StringComparer.OrdinalIgnoreCase);

        GameEquipmentFireOption? fire = enhancement.FireOptions.FirstOrDefault(option => option.Id == fireId);
        if (perfectHit && fire is { BonusProbability: > 0 } &&
            weights.ContainsKey(fire.BoostedEventId))
        {
            weights[fire.BoostedEventId] += fire.BonusProbability;
        }

        int nonBaseTotal = weights
            .Where(pair => !pair.Key.Equals(enhancement.BaseEventId, StringComparison.OrdinalIgnoreCase))
            .Sum(pair => pair.Value);
        if (weights.ContainsKey(enhancement.BaseEventId))
        {
            weights[enhancement.BaseEventId] = Math.Max(0, ProbabilityScale - nonBaseTotal);
        }

        return weights;
    }

    private GameEquipmentSimulationSlot CreateInitialSlot(GameEquipmentWashSlot definition)
    {
        GameEquipmentWashCandidate candidate = PickWeighted(definition.Candidates, value => value.Weight);
        int tierIndex = definition.IsWashable
            ? PickWeightedIndex(candidate.InitialTierWeights, candidate.TierTexts.Count)
            : 0;
        return new GameEquipmentSimulationSlot(definition, candidate, tierIndex);
    }

    private GameEquipmentEnhancementEvent PickEnhancementEvent(
        GameEquipmentEnhancementInfo enhancement,
        int fireId,
        bool perfectHit)
    {
        IReadOnlyDictionary<string, int> weights = GetEnhancementWeights(enhancement, fireId, perfectHit);
        return PickWeighted(
            enhancement.Events,
            gameEvent => weights.GetValueOrDefault(gameEvent.Id));
    }

    private int PickWeightedIndex(IReadOnlyList<int> weights, int itemCount)
    {
        int count = Math.Max(1, itemCount);
        int[] normalized = Enumerable.Range(0, count)
            .Select(index => index < weights.Count ? Math.Max(0, weights[index]) : 0)
            .ToArray();
        if (normalized.Sum() <= 0)
        {
            return 0;
        }

        return PickWeighted(Enumerable.Range(0, count).ToArray(), index => normalized[index]);
    }

    private T PickWeighted<T>(IReadOnlyList<T> values, Func<T, long> weightSelector)
    {
        if (values.Count == 0)
        {
            throw new InvalidOperationException("Cannot choose from an empty client-data table.");
        }

        long total = values.Sum(value => Math.Max(0, weightSelector(value)));
        if (total <= 0)
        {
            return values[0];
        }

        long roll = _random.NextInt64(total);
        foreach (T value in values)
        {
            roll -= Math.Max(0, weightSelector(value));
            if (roll < 0)
            {
                return value;
            }
        }

        return values[^1];
    }
}
