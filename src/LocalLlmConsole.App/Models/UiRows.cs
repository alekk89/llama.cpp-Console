using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LocalLlmConsole.Models;

public sealed class UiRow
{
    public string C1 { get; set; } = "";
    public string C2 { get; set; } = "";
    public string C3 { get; set; } = "";
    public string C4 { get; set; } = "";
    public string C5 { get; set; } = "";
    public string C6 { get; set; } = "";
    public string C7 { get; set; } = "";
    public string C8 { get; set; } = "";
    public string C9 { get; set; } = "";
    public string C10 { get; set; } = "";
    public string T1 { get; set; } = "";
    public string T2 { get; set; } = "";
    public string T3 { get; set; } = "";
    public string T4 { get; set; } = "";
    public string T5 { get; set; } = "";
    public bool B1 { get; set; } = true;
    public bool B2 { get; set; } = true;
    public bool B3 { get; set; } = true;
    public bool B4 { get; set; } = true;
    public bool B5 { get; set; } = true;
    public JsonObject Data { get; set; } = new();
}

public sealed class EditableSettingRow : INotifyPropertyChanged
{
    private bool _isSecretVisible;
    private string _type = "text";
    private string _value = "";

    public string Group { get; set; } = "";
    public string Label { get; set; } = "";
    public string Key { get; set; } = "";
    public string Type
    {
        get => _type;
        set
        {
            if (_type == value) return;
            _type = value;
            OnPropertyChanged();
            OnSecretActionPropertiesChanged();
        }
    }
    public string Action { get; set; } = "";
    public string ActionToolTip { get; set; } = "";
    public bool CanAction { get; set; }
    public string RevealAction => Type == "secret" ? (IsSecretVisible ? "Hide" : "Show") : "";
    public string RevealToolTip => Type == "secret"
        ? IsSecretVisible ? "Hide the full API key." : "Show the full API key in the settings grid."
        : "";
    public bool CanRevealAction => Type == "secret" && !string.IsNullOrWhiteSpace(Value);
    public string CopyAction => Type == "secret" ? "Copy" : "";
    public string CopyToolTip => Type == "secret" ? "Copy the full API key to the clipboard." : "";
    public bool CanCopyAction => Type == "secret" && !string.IsNullOrWhiteSpace(Value);
    public bool IsSecretVisible
    {
        get => _isSecretVisible;
        set
        {
            if (_isSecretVisible == value) return;
            _isSecretVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(RevealAction));
            OnPropertyChanged(nameof(RevealToolTip));
        }
    }
    public ObservableCollection<string> Options { get; } = new();
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(CanRevealAction));
            OnPropertyChanged(nameof(CanCopyAction));
        }
    }
    public string DisplayValue => Type == "secret" ? IsSecretVisible ? SecretDisplayValue(Value) : MaskSecret(Value) : Value;
    public JsonObject Data { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnSecretActionPropertiesChanged()
    {
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(RevealAction));
        OnPropertyChanged(nameof(RevealToolTip));
        OnPropertyChanged(nameof(CanRevealAction));
        OnPropertyChanged(nameof(CopyAction));
        OnPropertyChanged(nameof(CopyToolTip));
        OnPropertyChanged(nameof(CanCopyAction));
    }

    private static string SecretDisplayValue(string value)
    {
        var secret = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(secret) ? "not set" : secret;
    }

    private static string MaskSecret(string value)
    {
        var secret = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(secret)) return "not set";
        var suffix = secret.Length >= 4 ? secret[^4..] : "";
        return string.IsNullOrWhiteSpace(suffix) ? "********" : $"************{suffix}";
    }
}
