namespace IP6;

public class ObservableBool
{
    public ObservableBool(bool value)
    {
        this.Value = value;
    }
    private bool _value;

    public bool Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                ValueChanged?.Invoke(_value);
            }
        }
    }

    public event Action<bool> ValueChanged;
    public static implicit operator bool(ObservableBool observable) => observable.Value;

}
