using Application.ViewModels;

namespace Application.Services.MrShooferORS
{
    /// <summary>
    /// Mock API client for development when the real API is unavailable
    /// </summary>
    public class MockMrShooferAPIClient : MrShooferAPIClient
    {
        public MockMrShooferAPIClient() : base(new HttpClient(), "http://localhost")
        {
        }

        public new async Task<SearchedTrip> GetTripInfo(string tripcode)
        {
            // Return mock trip data
            await Task.Delay(100); // Simulate network delay

            return new SearchedTrip
            {
                tripPlanCode = tripcode,
                originCityName = "تهران",
                destinationCityName = "اصفهان",
                oringinLocationName = "ترمینال جنوب",
                destinationLocationName = "ترمینال صفه",
                startingDateTime = DateTime.Now.AddDays(1),
                taxiSupervisorName = "مستر شوفر",
                taxiSupervisorID = 1,
                carModelName = "پژو 405 | سمند",
                originalTicketprice = 500000,
                afterdiscticketprice = 450000
            };
        }

        public new async Task<List<SearchedTrip>> SearchTrips(DateTime startspan, DateTime endspan, int originCityId, int destinationCityid, int? originterminalId = null, int? destinationterminalid = null)
        {
            await Task.Delay(100);
            
            return new List<SearchedTrip>
            {
                new SearchedTrip
                {
                    tripPlanCode = "MOCK-TRIP-001",
                    originCityName = "تهران",
                    destinationCityName = "اصفهان",
                    startingDateTime = startspan,
                    taxiSupervisorName = "مستر شوفر",
                    carModelName = "پژو 405",
                    originalTicketprice = 500000,
                    afterdiscticketprice = 450000
                }
            };
        }

        public new async Task<string> ReserveTicketTemporarirly(TicketTempReserveRequestModel ticket)
        {
            await Task.Delay(100);
            return "MOCK-RESERVE-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
        }

        public new async Task<TicketConfirmationResponse> ConfirmReserve(ConfirmReserveRequestModel confirmreservemodel)
        {
            await Task.Delay(100);
            return new TicketConfirmationResponse
            {
                ticketCode = "MOCK-TICKET-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()
            };
        }
    }
}
