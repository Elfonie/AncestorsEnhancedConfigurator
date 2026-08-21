namespace AncestorsEnhanced.App.ViewModels;

internal sealed class UiMutationGate
{
    private readonly Lock _lock = new();
    private bool _isBusy;

    public event EventHandler? Changed;

    public bool IsBusy
    {
        get
        {
            lock (_lock)
            {
                return _isBusy;
            }
        }
    }

    public IDisposable? TryEnter()
    {
        lock (_lock)
        {
            if (_isBusy)
            {
                return null;
            }
            _isBusy = true;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return new Lease(this);
    }

    private void Exit()
    {
        lock (_lock)
        {
            _isBusy = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Lease(UiMutationGate owner) : IDisposable
    {
        private UiMutationGate? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
}
