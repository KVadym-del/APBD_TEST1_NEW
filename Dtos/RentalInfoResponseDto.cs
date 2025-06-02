using System;

namespace CarRentalApi.Dtos
{
    public class RentalInfoResponseDto
    {
        public string? Vin { get; set; }
        public string? Color { get; set; }
        public string? Model { get; set; }
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public int TotalPrice { get; set; }
    }
}