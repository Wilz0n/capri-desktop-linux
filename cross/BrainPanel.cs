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
        private readonly TextBox openRouterKey = new TextBox { PasswordChar = '•', Watermark = "OpenRouter API key (optional free fallback)" };
        private readonly TextBox geminiKey = new TextBox { PasswordChar = '•', Watermark = "Gemini API key (free AI Studio project)" };
        private readonly ComboBox openRouterModel = new ComboBox { MinWidth = 390 };
        private readonly ComboBox geminiModel = new ComboBox { MinWidth = 390 };
        private readonly ComboBox language = new ComboBox { MinWidth = 180 };
        private readonly ComboBox order = new ComboBox { MinWidth = 390 };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Look.Dim) };
        private readonly TextBlock quota = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Look.Dim) };

        internal BrainPanel(Pet pet)
        {
            this.pet = pet;
            Look.Overlay(this, "Capri Brain · Free providers");
            Width = 540;
            Height = 640;
            Background = new SolidColorBrush(Look.Panel);
            StackPanel stack = new StackPanel { Margin = new Thickness(22), Spacing = 9 };
            stack.Children.Add(new TextBlock { Text = "BRAIN · FREE PROVIDERS", FontFamily = Look.Mono, FontSize = 18, Foreground = new SolidColorBrush(Look.Cyan) });
            stack.Children.Add(new TextBlock { Text = "Gemini API is primary; OpenRouter :free is automatic backup. Paid OpenRouter models are never sent.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Look.Dim) });

            stack.Children.Add(new TextBlock { Text = "Gemini API key", Foreground = new SolidColorBrush(Look.Dim) });
            stack.Children.Add(geminiKey);
            foreach (string id in GeminiPool.Models) geminiModel.Items.Add(id);
            geminiModel.SelectedItem = pet.Saved.GeminiModel ?? GeminiPool.DefaultModel;
            stack.Children.Add(geminiModel);
            stack.Children.Add(new TextBlock { Text = "Create the key in Google AI Studio using a project with billing disabled. Gemini's free tier may use submitted content to improve Google products.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Look.Dim) });

            stack.Children.Add(new TextBlock { Text = "OpenRouter API key (optional backup)", Foreground = new SolidColorBrush(Look.Dim) });
            stack.Children.Add(openRouterKey);
            foreach (string id in OpenRouterPool.Requested) openRouterModel.Items.Add(id);
            string preferred = pet.Saved.OpenRouterModel ?? OpenRouterPool.PreferredDefaults[0];
            if (!OpenRouterPool.Requested.Contains(preferred)) openRouterModel.Items.Add(preferred);
            openRouterModel.SelectedItem = preferred;

            stack.Children.Add(new TextBlock { Text = "OpenRouter preferred free model", Foreground = new SolidColorBrush(Look.Dim) });
            stack.Children.Add(openRouterModel);

            stack.Children.Add(new TextBlock { Text = "Provider order", Foreground = new SolidColorBrush(Look.Dim) });
            order.Items.Add("Gemini first → OpenRouter backup");
            order.Items.Add("OpenRouter first → Gemini backup");
            order.SelectedIndex = string.Equals(pet.Saved.BrainOrder, "openrouter-first", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            stack.Children.Add(order);

            stack.Children.Add(new TextBlock { Text = "Reply language", Foreground = new SolidColorBrush(Look.Dim) });
            language.Items.Add("English");
            language.Items.Add("Español");
            language.SelectedItem = pet.Saved.ReplyLanguage == "es" ? "Español" : "English";
            stack.Children.Add(language);
            status.Text = ProviderStatus();
            stack.Children.Add(status);
            quota.Text = pet.OpenRouter == null ? "OpenRouter free quota not checked" : pet.OpenRouter.QuotaText();
            stack.Children.Add(quota);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            Button connect = new Button { Content = "Save and connect" };
            connect.Click += delegate { Connect(); };
            Button forgetGemini = new Button { Content = "Forget Gemini" };
            forgetGemini.Click += delegate { Forget("gemini"); };
            Button forgetOpenRouter = new Button { Content = "Forget OpenRouter" };
            forgetOpenRouter.Click += delegate { Forget("openrouter"); };
            buttons.Children.Add(connect);
            buttons.Children.Add(forgetGemini);
            buttons.Children.Add(forgetOpenRouter);
            stack.Children.Add(buttons);
            Content = stack;
        }

        private string ProviderStatus()
        {
            string gem = pet.Gemini == null ? "Gemini not connected" : pet.Gemini.StatusText();
            string or = pet.OpenRouter == null ? "OpenRouter not connected" : pet.OpenRouter.IsBlocked ? OpenRouterPool.DailyLimitMessage(pet.OpenRouter.BlockedUntil) : "OpenRouter connected · " + pet.OpenRouter.Models.Length + " verified free models";
            return gem + "\n" + or;
        }

        private void Connect()
        {
            string gem = (geminiKey.Text ?? "").Trim();
            string or = (openRouterKey.Text ?? "").Trim();
            string gemModel = geminiModel.SelectedItem as string;
            string orModel = openRouterModel.SelectedItem as string;
            if (gem.Length == 0 && or.Length == 0)
            {
                if (pet.Gemini == null) gem = BrainSecretStore.Load("gemini") ?? "";
                if (pet.OpenRouter == null) or = BrainSecretStore.Load("openrouter") ?? "";
            }
            if (gem.Length == 0 && or.Length == 0) { status.Text = "Enter at least one provider API key."; return; }
            if (gem.Length > 0 && !GeminiPool.IsAllowed(gemModel)) { status.Text = "Select one of the allowlisted free Gemini models."; return; }
            if (or.Length > 0 && !OpenRouterPool.IsFreeModel(orModel)) { status.Text = "Select a verified free OpenRouter model."; return; }
            try
            {
                if (gem.Length > 0) BrainSecretStore.Save("gemini", gem);
                if (or.Length > 0) BrainSecretStore.Save("openrouter", or);
                if (gem.Length > 0 || BrainSecretStore.Load("gemini") != null) pet.Gemini = new GeminiPool(gem.Length > 0 ? gem : BrainSecretStore.Load("gemini"), gemModel);
                if (or.Length > 0 || BrainSecretStore.Load("openrouter") != null)
                {
                    OpenRouterPool.Requested = OpenRouterCatalog.Order(OpenRouterPool.Requested, orModel);
                    pet.OpenRouter = OpenRouterPool.Automatic(or.Length > 0 ? or : BrainSecretStore.Load("openrouter"), orModel);
                    pet.Saved.OpenRouterModel = orModel;
                }
                pet.Saved.GeminiModel = gemModel;
                pet.Saved.BrainOrder = order.SelectedIndex == 1 ? "openrouter-first" : "gemini-first";
                pet.Saved.ReplyLanguage = string.Equals(language.SelectedItem as string, "Español", StringComparison.Ordinal) ? "es" : "en";
                pet.Saved.BrainProvider = "multi-free";
                pet.Brain.Model = gem.Length > 0 ? gemModel : pet.OpenRouter == null ? null : pet.OpenRouter.Models.FirstOrDefault();
                pet.Brain.Remote = null;
                pet.Brain.Forget();
                pet.Save();
                geminiKey.Text = "";
                openRouterKey.Text = "";
                status.Text = ProviderStatus();
                quota.Text = pet.OpenRouter == null ? "OpenRouter not configured" : "Checking OpenRouter free quota…";
                pet.RefreshOpenRouterQuota(pet.OpenRouter);
            }
            catch (Exception error) { status.Text = "Could not save provider settings: " + error.Message; }
        }

        private void Forget(string provider)
        {
            try { BrainSecretStore.Forget(provider); }
            catch (Exception) { }
            if (provider == "gemini") { pet.Gemini = null; pet.Saved.GeminiModel = null; }
            else if (provider == "openrouter")
            {
                pet.OpenRouter = null;
                pet.Saved.OpenRouterModel = null;
                pet.Saved.OpenRouterUseFreeRouter = false;
            }
            if (pet.Gemini == null && pet.OpenRouter == null) pet.Saved.BrainProvider = null;
            pet.Save();
            status.Text = ProviderStatus();
            quota.Text = pet.OpenRouter == null ? "OpenRouter free quota not checked" : pet.OpenRouter.QuotaText();
        }

        internal void UpdateQuota(string text)
        {
            quota.Text = text ?? "OpenRouter free quota unavailable";
            status.Text = ProviderStatus();
        }

        internal void UpdateProviderStatus()
        {
            status.Text = ProviderStatus();
        }
    }
}
