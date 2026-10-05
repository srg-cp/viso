using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Viso;

public partial class MainWindow
{
    private sealed class ShortcutEditor
    {
        public ModelShortcut Draft { get; init; } = new();
        public ModelShortcut Saved { get; set; } = new();
        public StackPanel Panel { get; init; } = new();
        public TextBox KeyBox { get; init; } = new();
        public TextBlock Status { get; init; } = new();
    }

    private readonly List<ShortcutEditor> shortcutEditors = new();

    private void AddModelShortcutClick(object sender, RoutedEventArgs e)
    {
        var used = shortcutEditors.Select(row => (row.Draft.Modifiers, row.Draft.VirtualKey)).ToHashSet();
        used.UnionWith(new[] { (draftModifiers, draftVirtualKey), (draftShortModifiers, draftShortVirtualKey), (draftHideModifiers, draftHideVirtualKey) });
        var shortcut = new ModelShortcut { Provider = activeProvider, Model = CurrentModel };
        int available = Enumerable.Range(0x30, 0x5A - 0x30 + 1)
            .Where(key => key <= 0x39 || key >= 0x41).FirstOrDefault(key => !used.Contains((5u, key)));
        if (available == 0) { StatusText.Text = "No quedan combinaciones sugeridas. Edita o elimina un atajo."; return; }
        shortcut.VirtualKey = available;
        AddShortcutEditor(shortcut);
        StatusText.Text = "Sin guardar.";
    }

    private void AddShortcutEditor(ModelShortcut shortcut)
    {
        var row = new ShortcutEditor { Draft = shortcut.Copy(), Saved = shortcut.Copy() };
        row.Panel.Margin = new Thickness(0, 8, 0, 12);
        TextBlock Label(string text) => new() { Text = text, Style = (Style)FindResource("FieldLabel") };
        row.Panel.Children.Add(Label("Modelo para este atajo"));
        var models = new ComboBox { IsEditable = false };
        foreach (var choice in ModelShortcut.Choices) models.Items.Add(choice);
        var selected = ModelShortcut.Choices.FirstOrDefault(choice => choice.Provider == shortcut.Provider && choice.Model == shortcut.Model);
        if (selected == null) { selected = shortcut.Copy(); models.Items.Add(selected); }
        models.SelectedItem = selected;
        models.SelectionChanged += (_, _) =>
        {
            if (models.SelectedItem is ModelShortcut choice)
            { row.Draft.Provider = choice.Provider; row.Draft.Model = choice.Model; row.Status.Text = "Sin guardar."; }
        };
        row.Panel.Children.Add(models);
        row.Panel.Children.Add(Label("Combinación de teclas"));
        row.KeyBox.IsReadOnly = true;
        row.KeyBox.Text = GlobalHotkey.Label(shortcut.Modifiers, shortcut.VirtualKey);
        row.KeyBox.ToolTip = "Presiona la combinación que quieras usar. Esc para cancelar.";
        row.KeyBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            if (key == Key.Escape)
            { row.Draft.Modifiers = row.Saved.Modifiers; row.Draft.VirtualKey = row.Saved.VirtualKey; }
            else
            {
                uint modifiers = 0;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 1;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 2;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= 8;
                int virtualKey = KeyInterop.VirtualKeyFromKey(key);
                if (!GlobalHotkey.IsValid(modifiers, virtualKey))
                { row.Status.Text = "Combina Ctrl, Alt, Mayús o Windows con otra tecla."; return; }
                row.Draft.Modifiers = modifiers; row.Draft.VirtualKey = virtualKey;
            }
            row.KeyBox.Text = GlobalHotkey.Label(row.Draft.Modifiers, row.Draft.VirtualKey);
            row.Status.Text = "Sin guardar. Esc para cancelar.";
        };
        row.Panel.Children.Add(row.KeyBox);
        var options = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var shortAnswer = new CheckBox { Content = "Respuesta recortada", IsChecked = shortcut.ShortAnswer, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        shortAnswer.Checked += (_, _) => { row.Draft.ShortAnswer = true; row.Status.Text = "Sin guardar."; };
        shortAnswer.Unchecked += (_, _) => { row.Draft.ShortAnswer = false; row.Status.Text = "Sin guardar."; };
        options.Children.Add(shortAnswer);
        var remove = new Button { Content = "Eliminar" };
        remove.Click += (_, _) => { shortcutEditors.Remove(row); ModelShortcutsPanel.Children.Remove(row.Panel); StatusText.Text = "Sin guardar."; };
        options.Children.Add(remove);
        row.Panel.Children.Add(options);
        row.Status.Style = (Style)FindResource("Caption");
        row.Status.Margin = new Thickness(0, 7, 0, 0);
        row.Panel.Children.Add(row.Status);
        shortcutEditors.Add(row);
        ModelShortcutsPanel.Children.Add(row.Panel);
    }

    private void UpdateModelShortcutDisplay()
    {
        foreach (var row in shortcutEditors)
        {
            bool active = hotkeys?.Models.Any(item => item.Hotkey.Registered &&
                item.Shortcut.Provider == row.Draft.Provider && item.Shortcut.Model == row.Draft.Model &&
                item.Shortcut.ShortAnswer == row.Draft.ShortAnswer &&
                item.Hotkey.Modifiers == row.Draft.Modifiers && item.Hotkey.VirtualKey == row.Draft.VirtualKey) == true;
            row.Status.Text = active ? "Activo: " + row.Draft : "Sin activar. Pulsa Guardar; si está ocupado, elige otro atajo.";
        }
    }
}
