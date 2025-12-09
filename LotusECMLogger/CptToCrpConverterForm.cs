using LotusECMLogger.Controls;

namespace LotusECMLogger
{
	public partial class CptToCrpConverterForm : Form
	{
		public CptToCrpConverterForm()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			Text = "CPT to CRP Converter";
			Size = new Size(650, 350);
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;

			var control = new CptToCrpConverterControl
			{
				Dock = DockStyle.Fill
			};

			Controls.Add(control);
		}
	}
}
