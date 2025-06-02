using System;

namespace CarRentalApi.Dtos
{
    public class AddClientWithRentalRequestDto
    {
        public ClientRequestDto? Client { get; set; }
        public int CarId { get; set; }
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
    }
}