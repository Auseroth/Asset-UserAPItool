using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App.ViewModels;

/// <summary>
/// ViewModel for a single row in the field mapping two-column list.
/// SourceField is the input side - an AD attribute name for AD sources,
/// or an API field name for cloud/file sources.
/// </summary>
public sealed class FieldMappingViewModel : ViewModelBase
{
    public FieldMappingViewModel() { }

    public FieldMappingViewModel(FieldMapping model)
    {
        CloudField = model.CloudField;
        SourceField = model.SourceFields.Count > 0 ? model.SourceFields[0] : string.Empty;
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

    private string _sourceField = string.Empty;
    public string SourceField
    {
        get => _sourceField;
        set
        {
            if (SetProperty(ref _sourceField, value))
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
    /// Sample source record used to generate the live preview.
    /// Set by the parent VM when source preview data is available.
    /// Works for AD records and cloud API records alike.
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
    /// Uses sample source data if available, otherwise shows a structural placeholder.
    /// </summary>
    public string Preview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CloudField) && string.IsNullOrWhiteSpace(SourceField))
                return "";

            if (_sampleRecord is not null && _sampleRecord.Count > 0)
                return ComputeLivePreview(_sampleRecord);

            return ComputeStructuralPreview();
        }
    }

    private string ComputeLivePreview(Dictionary<string, string> sample)
    {
        try
        {
            var mapping = ToModel();

            if (!string.IsNullOrWhiteSpace(mapping.TransformExpression))
            {
                var result = TransformEngine.PreviewTransform(sample, mapping);
                return string.IsNullOrEmpty(result)
                    ? (DefaultValue.Length > 0 ? $"\u2192 \"{DefaultValue}\" (default)" : "\u2192 (empty)")
                    : $"\u2192 \"{result}\"";
            }

            if (mapping.SourceFields.Count > 0 &&
                sample.TryGetValue(mapping.SourceFields[0], out var value) &&
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

    private string ComputeStructuralPreview()
    {
        if (!string.IsNullOrWhiteSpace(TransformExpression))
        {
            var fields = TransformEngine.ExtractAttributeNames(TransformExpression);
            return fields.Count > 0
                ? $"\u2192 {TransformExpression}"
                : $"\u2192 \"{TransformExpression}\" (static)";
        }

        if (!string.IsNullOrWhiteSpace(SourceField))
            return $"\u2192 [{SourceField}]";

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
            mapping.SourceFields = TransformEngine.ExtractAttributeNames(TransformExpression).ToList();
        }
        else
        {
            mapping.SourceFields = string.IsNullOrWhiteSpace(SourceField)
                ? []
                : [SourceField];
        }

        return mapping;
    }
}