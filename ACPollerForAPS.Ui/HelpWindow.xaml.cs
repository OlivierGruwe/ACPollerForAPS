using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.Controls;

namespace PipelineConfigWpf
{
    public partial class HelpWindow : MetroWindow
    {
        // une bulle du fil de conversation
        public class ChatBubble
        {
            public string Text { get; set; }
            public HorizontalAlignment Align { get; set; }
            public Brush BubbleBrush { get; set; }
            public Brush Fore { get; set; }
        }

        private readonly ObservableCollection<ChatBubble> _chat = new ObservableCollection<ChatBubble>();

        public HelpWindow()
        {
            InitializeComponent();
            ChatList.ItemsSource = _chat;
            SuggestionsList.ItemsSource = HelpBot.SuggestedQuestions;

            AddBot("Hi! Ask me a question about the configuration, "
                 + "or pick a frequently asked question below.");
        }

        private void Send_Click(object sender, RoutedEventArgs e) => SendCurrent();

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { SendCurrent(); e.Handled = true; }
        }

        private void Suggestion_Click(object sender, RoutedEventArgs e)
        {
            var q = ((System.Windows.Controls.Button)sender).Content?.ToString();
            if (string.IsNullOrWhiteSpace(q)) return;
            Ask(q);
        }

        private void SendCurrent()
        {
            var q = InputBox.Text;
            if (string.IsNullOrWhiteSpace(q)) return;
            InputBox.Clear();
            Ask(q);
        }

        private void Ask(string question)
        {
            AddUser(question);
            AddBot(HelpBot.Answer(question));
        }

        private void AddUser(string text)
        {
            _chat.Add(new ChatBubble
            {
                Text = text,
                Align = HorizontalAlignment.Right,
                BubbleBrush = (Brush)FindResource("MahApps.Brushes.Accent"),
                Fore = Brushes.White
            });
            ScrollToEnd();
        }

        private void AddBot(string text)
        {
            _chat.Add(new ChatBubble
            {
                Text = text,
                Align = HorizontalAlignment.Left,
                BubbleBrush = (Brush)FindResource("MahApps.Brushes.Gray8"),
                Fore = (Brush)FindResource("MahApps.Brushes.ThemeForeground")
            });
            ScrollToEnd();
        }

        private void ScrollToEnd()
        {
            ChatScroll.ScrollToEnd();
        }
    }
}
