using DevExpress.Mvvm;

namespace RLC_LoadBank_SeparateVer.ViewModels
{
    /// <summary>Content shown for nav sections that are not built yet (PlaceholderView).</summary>
    public class PlaceholderViewModel : ViewModelBase
    {
        public string Title { get; }
        public string Caption { get; }

        public PlaceholderViewModel(string title, string caption = null)
        {
            Title = title;
            Caption = caption ?? "준비 중입니다.";
        }
    }
}
