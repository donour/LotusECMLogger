using System.ComponentModel;

namespace LotusECMLogger.Controls
{
    public partial class DTCControl : UserControl
    {
        public DTCControl()
        {
            InitializeComponent();
        }

        private void readCodesButton_Click(object sender, EventArgs e)
        {
            // TODO: Implement DTC reading functionality
            MessageBox.Show("Read Codes functionality not yet implemented", "Not Implemented",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void clearCodesButton_Click(object sender, EventArgs e)
        {
            // TODO: Implement DTC clearing functionality
            MessageBox.Show("Clear Codes functionality not yet implemented", "Not Implemented",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
