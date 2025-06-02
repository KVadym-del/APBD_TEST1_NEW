using System.Collections.Generic;

namespace CarRentalApi.Dtos
{
    public class ClientDetailsResponseDto
    {
        public int Id { get; set; }
        public string? FirstName { get; set; } = string.Empty;
        public string? LastName { get; set; } = string.Empty;
        public string? Address { get; set; } = string.Empty;
        public List<RentalInfoResponseDto> Rentals { get; set; }

        public ClientDetailsResponseDto()
        {
            Rentals = new List<RentalInfoResponseDto>();
        }
    }
}