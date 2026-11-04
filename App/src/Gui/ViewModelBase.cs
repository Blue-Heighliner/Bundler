namespace BlueHeighliner.Bundler;

/// <summary>
/// Base of the view models: raises <see cref="INotifyPropertyChanged.PropertyChanged" /> when a property changes.
/// </summary>
internal abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Changes a field and raises the event when the value is different.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="field">The backing field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="name">The property name, filled in by the compiler.</param>
    /// <returns><see langword="true" /> when the value changed.</returns>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    /// <summary>
    /// Raises the event for a property.
    /// </summary>
    /// <param name="name">The property name, filled in by the compiler.</param>
    protected void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new(name));
}
