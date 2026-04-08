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
        set => SetProperty(ref _cloudField, value);
    }

    private string _adAttribute = string.Empty;
    public string AdAttribute
    {
        get => _adAttribute;
        set => SetProperty(ref _adAttribute, value);
    }

    private bool _isTransform;
    public bool IsTransform
    {
        get => _isTransform;
        set => SetProperty(ref _isTransform, value);
    }

    private string _transformExpression = string.Empty;
    public string TransformExpression
    {
        get => _transformExpression;
        set
        {
            if (SetProperty(ref _transformExpression, value))
            {
                // Validate expression on change
                TransformError = TransformEngine.ValidateExpression(value);
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
        set => SetProperty(ref _defaultValue, value);
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