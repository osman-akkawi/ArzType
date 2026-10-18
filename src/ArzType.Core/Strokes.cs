namespace ArzType.Core;

/// <summary>A physical key press: scan code plus the case modifiers that were active.</summary>
public readonly record struct Stroke(uint ScanCode, bool Shift, bool CapsLock);

/// <summary>Turns a physical key into the text a given keyboard layout would produce.</summary>
public interface IKeyMapper
{
    /// <summary>Returns the produced text, or null for dead keys / keys without text output.</summary>
    string? Map(Stroke stroke, nint layout);
}
