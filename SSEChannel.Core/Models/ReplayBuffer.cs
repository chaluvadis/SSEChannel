namespace SSEChannel.Core.Models;

/// <summary>Thread-safe circular buffer for SSE event replay.</summary>
public sealed class ReplayBuffer
{
    private readonly SseMessage[] _buffer;
    private readonly int _capacity;
    private int _head;  // index where next write goes
    private int _count;
    private readonly object _lock = new();

    public ReplayBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _buffer = new SseMessage[capacity];
    }

    public void Add(SseMessage message)
    {
        lock (_lock)
        {
            _buffer[_head] = message;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
        }
    }

    /// <summary>Returns messages newer than lastEventId (exclusive), in chronological order.</summary>
    public IEnumerable<SseMessage> GetSince(string? lastEventId)
    {
        lock (_lock)
        {
            if (_count == 0) return [];

            // Oldest item is at (_head - _count + _capacity) % _capacity
            var snapshot = new SseMessage[_count];
            var start = (_head - _count + _capacity) % _capacity;
            for (int i = 0; i < _count; i++)
                snapshot[i] = _buffer[(start + i) % _capacity];

            if (string.IsNullOrEmpty(lastEventId))
                return snapshot;

            var idx = Array.FindIndex(snapshot, m => m.Id == lastEventId);
            if (idx < 0) return snapshot; // ID not found, replay all
            return snapshot.Skip(idx + 1);
        }
    }

    public int Count { get { lock (_lock) return _count; } }
}
