namespace Vidcron.Config
{
    public class EmailConfig
    {
        public string FromAddress { get; set; }

        public bool SmtpIsSsl { get; set; }
        
        public string SmtpPassword { get; set; }
        
        public int SmtpPort { get; set; }
        
        public string SmtpServer { get; set; }
        
        public string SmtpUsername { get; set; }

        public string ToAddress { get; set; }
    }
}