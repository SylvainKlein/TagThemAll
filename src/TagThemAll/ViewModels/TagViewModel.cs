using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using System.Windows.Media;
using TagThemAll.Models;

namespace TagThemAll.ViewModels;

public class TagViewModel : ViewModelBase
{
    public TagItem Model { get; }

    public long Id => Model.Id;
    public string Name => Model.Name;

    private SolidColorBrush _brush;
    public SolidColorBrush Brush
    {
        get => _brush;
        private set => SetProperty(ref _brush, value);
    }

    private SolidColorBrush _borderBrush;
    public SolidColorBrush BorderBrush
    {
        get => _borderBrush;
        private set => SetProperty(ref _borderBrush, value);
    }

    public ICommand? RemoveCommand { get; set; }

    public TagViewModel(TagItem model, Action<TagViewModel>? onRemove = null)
    {
        Model = model;
        if (onRemove != null)
        {
            RemoveCommand = new RelayCommand(() => onRemove(this));
        }

        var (bg, border) = GenerateColorsForTag(model.Name, model.ColorHex);
        _brush = new SolidColorBrush(bg);
        _brush.Freeze();
        _borderBrush = new SolidColorBrush(border);
        _borderBrush.Freeze();
    }

    private static readonly (Color Bg, Color Border)[] PredefinedPalettes = new[]
    {
        (Color.FromArgb(200, 30, 58, 138), Color.FromArgb(255, 59, 130, 246)),   // Blue
        (Color.FromArgb(200, 6, 78, 59), Color.FromArgb(255, 16, 185, 129)),     // Emerald
        (Color.FromArgb(200, 88, 28, 135), Color.FromArgb(255, 168, 85, 247)),   // Purple
        (Color.FromArgb(200, 120, 53, 15), Color.FromArgb(255, 245, 158, 11)),   // Amber
        (Color.FromArgb(200, 136, 19, 55), Color.FromArgb(255, 244, 63, 94)),    // Rose
        (Color.FromArgb(200, 19, 78, 74), Color.FromArgb(255, 20, 184, 166)),    // Teal
        (Color.FromArgb(200, 67, 20, 7), Color.FromArgb(255, 234, 88, 12)),     // Orange
        (Color.FromArgb(200, 49, 46, 129), Color.FromArgb(255, 99, 102, 241)),   // Indigo
        (Color.FromArgb(200, 80, 7, 36), Color.FromArgb(255, 236, 72, 153)),    // Pink
        (Color.FromArgb(200, 22, 78, 99), Color.FromArgb(255, 6, 182, 212)),     // Cyan
    };

    public static (Color Bg, Color Border) GenerateColorsForTag(string tagName, string? customHex = null)
    {
        if (!string.IsNullOrWhiteSpace(customHex))
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(customHex);
                return (Color.FromArgb(180, c.R, c.G, c.B), c);
            }
            catch { }
        }

        int hash = Math.Abs(tagName.Trim().ToLowerInvariant().GetHashCode());
        int index = hash % PredefinedPalettes.Length;
        return PredefinedPalettes[index];
    }
}
