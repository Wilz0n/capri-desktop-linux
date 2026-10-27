using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CapriDesktop
{
    internal sealed class BrainPanel : Window
    {
        private readonly Pet pet;
        private readonly TextBox key = new TextBox { PasswordChar = '•', Watermark = "OpenRouter API key" };
        private readonly ComboBox model = new ComboBox { MinWidth = 380 };
        private readonly ComboBox language = new ComboBox { MinWidth = 180 };
        private readonly CheckBox useFreeRouter = new CheckBox { Content = "Use OpenRouter Free router (blocked if zero-price routing cannot be guaranteed)" };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Look.Dim) };
        private readonly TextBlock quota = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Look.Dim) };

        internal BrainPanel(Pet pet)
        {
            this.pet = pet;
            Look.Overlay(this, "Capri Brain · OpenRouter");
            Width = 520;
            Height = 320;
            Background = new SolidColorBrush(Look.Panel);
            StackPanel stack = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
            stack.Children.Add(new TextBlock { Text = "OPENROUTER · FREE POOL", FontFamily = Look.Mono, FontSize = 18, Foreground = new SolidColorBrush(Look.Cyan) });
            stack.Children.Add(new TextBlock { Text = "Only verified zero-price :free models are sent. Capri never falls back to paid models.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Look.Dim) });
            stack.Children.Add(key);
            foreach (string id in OpenRouterPool.Requested) model.Items.Add(id);
            string preferred = pet.Saved.OpenRouterModel ?? OpenRouterPool.PreferredDefaults[0];
            if (!OpenRouterPool.Requested.Contains(preferred)) model.Items.Add(preferred);
            model.SelectedItem = preferred;
            stack.Children.Add(new TextBlock { Text = "Preferred model (tried first)", Foreground = new SolidColorBrush(Look.Dim) });
            stack.Children.Add(model);
            stack.Children.Add(new TextBlock { Text = "Automatic free-model fallback pool", Foreground = new SolidColorBrush(Look.Dim) });
            useFreeRouter.IsEnabled = false;
            stack.Children.Add(new TextBlock { Text = "Reply language", Foreground = new SolidColorBrush(Look.Dim) });
            language.Items.Add("English");
            language.Items.Add("Español");
            language.SelectedItem = pet.Saved.ReplyLanguage == "es" ? "Español" : "English";
            stack.Children.Add(language);
            useFreeRouter.IsChecked = pet.Saved.OpenRouterUseFreeRouter;
            stack.Children.Add(useFreeRouter);
            status.Text = pet.OpenRouter == null ? "Not connected" : "Connected · " + pet.OpenRouter.Models.FirstOrDefault();
            stack.Children.Add(status);
            quota.Text = pet.OpenRouter == null ? "Free requests quota not checked" : pet.OpenRouter.QuotaText();
            stack.Children.Add(quota);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            Button connect = new Button { Content = "Connect" };
            connect.Click += delegate { Connect(); };
            Button forget = new Button { Content = "Forget" };
            forget.Click += delegate { Forget(); };
            buttons.Children.Add(connect);
            buttons.Children.Add(forget);
            stack.Children.Add(buttons);
            Content = stack;
        }

        private void Connect()
        {
            string secret = (key.Text ?? "").Trim();
            if (secret.Length == 0) { status.Text = "Enter the API key from OpenRouter settings."; return; }
            string selected = model.SelectedItem as string;
            bool router = useFreeRouter.IsChecked == true;
            if (string.IsNullOrWhiteSpace(selected) || !OpenRouterPool.IsFreeModel(selected)) { status.Text = "Select a verified free model ID."; return; }
            try
            {
                OpenRouterPool.Requested = OpenRouterCatalog.Order(OpenRouterPool.Requested, selected);
                BrainSecretStore.Save("openrouter", secret);
                if (router)
                {
                    status.Text = "The router mode cannot guarantee the zero-price ceiling. Select an individual free model with automatic fallbacks instead.";
                    return;
                }
                pet.OpenRouter = OpenRouterPool.Automatic(secret, selected);
                pet.Saved.OpenRouterModel = selected;
                pet.Saved.OpenRouterUseFreeRouter = false;
                pet.Saved.ReplyLanguage = string.Equals(language.SelectedItem as string, "Español", StringComparison.Ordinal) ? "es" : "en";
                pet.Saved.BrainProvider = "openrouter";
                pet.Brain.Model = selected;
                pet.Saved.Model = selected;
                pet.Brain.Remote = null;
                pet.Brain.Forget();
                pet.Save();
                key.Text = "";
                status.Text = "Connected · preferred " + selected + " · " + pet.OpenRouter.Models.Length + " free models (zero-price verified)";
                quota.Text = "Checking free requests remaining…";
                pet.RefreshOpenRouterQuota(pet.OpenRouter);
            }
            catch (Exception error) { status.Text = "Could not save OpenRouter key: " + error.Message; }
        }

        private void Forget()
        {
            try { BrainSecretStore.Forget("openrouter"); }
            catch (Exception) { }
            pet.OpenRouter = null;
            pet.Saved.OpenRouterModel = null;
            pet.Saved.OpenRouterUseFreeRouter = false;
            pet.Saved.BrainProvider = null;
            pet.Brain.Model = null;
            pet.Brain.Forget();
            pet.Save();
            key.Text = "";
            status.Text = "OpenRouter key forgotten.";
            quota.Text = "Free requests quota not checked";
        }

        internal void UpdateQuota(string text)
        {
            quota.Text = text ?? "Free requests quota unavailable";
        }
    }
}
