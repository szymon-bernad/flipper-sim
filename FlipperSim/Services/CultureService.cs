using System.Globalization;
using Microsoft.JSInterop;

namespace FlipperSim.Services;

public class CultureService
{
    private const string CULTURE_STORAGE_KEY = "FlipperSim.SelectedCulture";
    
    public event Action? CultureChanged;
    
    private CultureInfo _currentCulture = CultureInfo.CurrentCulture;
    
    public CultureInfo CurrentCulture 
    { 
        get => _currentCulture;
        private set
        {
            if (_currentCulture != value)
            {
                _currentCulture = value;
                CultureInfo.CurrentCulture = value;
                CultureInfo.CurrentUICulture = value;
                CultureChanged?.Invoke();
            }
        }
    }
    
    public void SetCulture(string cultureName)
    {
        CurrentCulture = new CultureInfo(cultureName);
    }
    
    public List<CultureInfo> GetSupportedCultures()
    {
        return new List<CultureInfo>
        {
            new("en-US"),
            new("pl-PL")
        };
    }
    
    public bool IsCultureSelected { get; private set; }
    
    public void MarkCultureAsSelected()
    {
        IsCultureSelected = true;
    }
}
