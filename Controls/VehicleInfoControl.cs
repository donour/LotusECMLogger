using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    public partial class VehicleInfoControl : UserControl
    {
        private readonly IVehicleInfoService _vehicleInfoService;
        private List<VehicleParameterReading> vehicleDataSnapshot = [];

        public VehicleInfoControl()
        {
            InitializeComponent();
            _vehicleInfoService = new VehicleInfoService();
        }

        private void readDataButton_Click(object sender, EventArgs e)
        {
            LoadVehicleData();
        }

        private void LoadVehicleData()
        {
            try
            {
                readDataButton.Enabled = false;
                readDataButton.Text = "Loading...";

                // Clear previous data
                vehicleDataSnapshot.Clear();

                // Load data from service
                vehicleDataSnapshot = _vehicleInfoService.LoadVehicleData();

                // Update the UI
                UpdateVehicleInfoView();

                MessageBox.Show($"Successfully loaded {vehicleDataSnapshot.Count} vehicle data points!",
                    "Load Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading vehicle data: {ex.Message}", "Load Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                readDataButton.Enabled = true;
                readDataButton.Text = "Load Vehicle Data";
            }
        }

        private void UpdateVehicleInfoView()
        {
            vehicleInfoView.BeginUpdate();
            vehicleInfoView.Items.Clear();

            foreach (var reading in vehicleDataSnapshot)
            {
                var item = new ListViewItem(reading.Name);
                item.SubItems.Add(reading.Value.ToString("F2"));
                item.SubItems.Add(reading.Unit);

                vehicleInfoView.Items.Add(item);
            }

            vehicleInfoView.EndUpdate();
        }
    }
}
