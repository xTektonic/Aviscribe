namespace Aviscribe.Core.KingdomDetection;

public sealed class KingdomDetectionTracker
{
    public static readonly TimeSpan DefaultInspectionInterval =
        TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultConfirmationWindow =
        TimeSpan.FromSeconds(10);

    private readonly TimeSpan _inspectionInterval;
    private readonly TimeSpan _confirmationWindow;
    private readonly int _requiredMatches;

    private DateTime? _lastInspection;
    private string? _candidateKingdom;
    private DateTime _lastCandidateObservation;
    private int _candidateMatches;

    public KingdomDetectionTracker(
        TimeSpan? inspectionInterval = null,
        TimeSpan? confirmationWindow = null,
        int requiredMatches = 2)
    {
        _inspectionInterval = inspectionInterval ?? DefaultInspectionInterval;
        _confirmationWindow = confirmationWindow ?? DefaultConfirmationWindow;
        if (_inspectionInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(inspectionInterval));
        if (_confirmationWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(confirmationWindow));
        if (requiredMatches < 1)
            throw new ArgumentOutOfRangeException(nameof(requiredMatches));

        _requiredMatches = requiredMatches;
    }

    public bool ShouldInspect(DateTime timestamp)
    {
        if (_lastInspection == null ||
            timestamp < _lastInspection.Value ||
            timestamp - _lastInspection.Value >= _inspectionInterval)
        {
            _lastInspection = timestamp;
            ExpireCandidate(timestamp);
            return true;
        }

        return false;
    }

    public string? Observe(
        KingdomDetectionResult result,
        DateTime timestamp,
        string currentKingdom)
    {
        ExpireCandidate(timestamp);
        if (!result.IsMatch || result.Kingdom == null)
            return null;

        if (result.Kingdom.Equals(
                currentKingdom,
                StringComparison.OrdinalIgnoreCase))
        {
            ResetCandidate();
            return null;
        }

        if (!_candidateKingdom?.Equals(
                result.Kingdom,
                StringComparison.OrdinalIgnoreCase) ?? true)
        {
            _candidateKingdom = result.Kingdom;
            _candidateMatches = 1;
        }
        else
        {
            _candidateMatches++;
        }

        _lastCandidateObservation = timestamp;
        if (_candidateMatches < _requiredMatches)
            return null;

        var confirmed = _candidateKingdom;
        ResetCandidate();
        return confirmed;
    }

    public void Reset()
    {
        _lastInspection = null;
        ResetCandidate();
    }

    public void ResetCandidate()
    {
        _candidateKingdom = null;
        _candidateMatches = 0;
        _lastCandidateObservation = default;
    }

    private void ExpireCandidate(DateTime timestamp)
    {
        if (_candidateKingdom != null &&
            (timestamp < _lastCandidateObservation ||
             timestamp - _lastCandidateObservation > _confirmationWindow))
        {
            ResetCandidate();
        }
    }
}
