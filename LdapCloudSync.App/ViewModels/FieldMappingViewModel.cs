using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App.ViewModels;

/// <summary>
/// ViewModel for a single row in the field mapping two-column list.
/// </summary>
public sealed class FieldMappingViewModel : ViewModelBase
{
    public FieldMappingViewModel() { }

    public FieldMappingViewModel(FieldMapping model)
    {
        CloudField = model.CloudField;
        AdAttribute = model.AdAttributes.Count > 0 ? model.AdAttributes[0] : string.Empty;
        TransformExpression = model.TransformExpression ?? string.Empty;
        DefaultValue = model.DefaultValue ?? string.Empty;
        IsTransform = !string.IsNullOrWhiteSpace(model.TransformExpression);
    }

    private string _cloudField = string.Empty;
    public string CloudField
    {
        get => _cloudField;
        set
        {
            if (SetProperty(ref _cloudField, value))
                OnPropertyChanged(nameof(Preview));
        }
    }

    private string _adAttribute = string.Empty;
    public string AdAttribute
    {
        get => _adAttribute;
        set
        {
            if (SetProperty(ref _adAttribute, value))
                OnPropertyChanged(nameof(Preview));
        }
    }

    private bool _isTransform;
    public bool IsTransform
    {
        get => _isTransform;
        set
        {
            if (SetProperty(ref _isTransform, value))
                OnPropertyChanged(nameof(Preview));
        }
    }

    private string _transformExpression = string.Empty;
    public string TransformExpression
    {
        get => _transformExpression;
        set
        {
            if (SetProperty(ref _transformExpression, value))
            {
                TransformError = TransformEngine.ValidateExpression(value);
                OnPropertyChanged(nameof(Preview));
            }
        }
    }

    private string? _transformError;
    public string? TransformError
    {
        get => _transformError;
        set => SetProperty(ref _transformError, value);
    }

    private string _defaultValue = string.Empty;
    public string DefaultValue
    {
        get => _defaultValue;
        set
        {
            if (SetProperty(ref _defaultValue, value))
                OnPropertyChanged(nameof(Preview));
        }
    }

    /// <summary>
    /// Sample AD record used to generate the live preview.
    /// Set by the parent VM when AD preview data is available.
    /// </summary>
    private Dictionary<string, string>? _sampleRecord;
    public Dictionary<string, string>? SampleRecord
    {
        get => _sampleRecord;
        set
        {
            _sampleRecord = value;
            OnPropertyChanged(nameof(Preview));
        }
    }

    /// <summary>
    /// Live preview showing what value this mapping will produce.
    /// Uses sample AD data if available, otherwise shows a placeholder pattern.
    /// </summary>
    public string Preview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CloudField) && string.IsNullOrWhiteSpace(AdAttribute))
                return "";

            // If we have real sample data, compute the actual result
            if (_sampleRecord is not null && _sampleRecord.Count > 0)
            {
                return ComputeLivePreview(_sampleRecord);
            }

            // No sample data — show a structural preview
            return ComputeStructuralPreview();
        }
    }

    /// <summary>
    /// Computes preview using a real AD record sample.
    /// </summary>
    private string ComputeLivePreview(Dictionary<string, string> sample)
    {
        try
        {
            var mapping = ToModel();

            if (!string.IsNullOrWhiteSpace(mapping.TransformExpression))
            {
                // Replay the transform expression against sample data
                var result = TransformEngine.PreviewTransform(sample, mapping);
                return string.IsNullOrEmpty(result)
                    ? (DefaultValue.Length > 0 ? $"\u2192 \"{DefaultValue}\" (default)" : "\u2192 (empty)")
                    : $"\u2192 \"{result}\"";
            }

            // Direct 1:1 mapping
            if (mapping.AdAttributes.Count > 0 &&
                sample.TryGetValue(mapping.AdAttributes[0], out var value) &&
                !string.IsNullOrEmpty(value))
            {
                return $"\u2192 \"{value}\"";
            }

            return DefaultValue.Length > 0
                ? $"\u2192 \"{DefaultValue}\" (default)"
                : "\u2192 (no value in sample)";
        }
        catch
        {
            return "\u2192 (error)";
        }
    }

    /// <summary>
    /// Shows a structural preview when no sample AD data is available.
    /// </summary>
    private string ComputeStructuralPreview()
    {
        if (!string.IsNullOrWhiteSpace(TransformExpression))
        {
            // Show the expression pattern, e.g., {givenName} {sn}
            var attrs = TransformEngine.ExtractAttributeNames(TransformExpression);
            return attrs.Count > 0
                ? $"\u2192 {TransformExpression}"
                : $"\u2192 \"{TransformExpression}\" (static)";
        }

        if (!string.IsNullOrWhiteSpace(AdAttribute))
            return $"\u2192 [{AdAttribute}]";

        if (!string.IsNullOrWhiteSpace(DefaultValue))
            return $"\u2192 \"{DefaultValue}\" (default)";

        return "";
    }

    public FieldMapping ToModel()
    {
        var mapping = new FieldMapping
        {
            CloudField = CloudField,
            DefaultValue = string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue
        };

        if (IsTransform && !string.IsNullOrWhiteSpace(TransformExpression))
        {
            mapping.TransformExpression = TransformExpression;
            mapping.AdAttributes = TransformEngine.ExtractAttributeNames(TransformExpression).ToList();
        }
        else
        {
            mapping.AdAttributes = string.IsNullOrWhiteSpace(AdAttribute)
                ? []
                : [AdAttribute];
        }

        return mapping;
    }
}