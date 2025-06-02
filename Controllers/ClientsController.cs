using CarRentalApi.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CarRentalApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClientsController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public ClientsController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        [HttpGet("{clientId}")]
        public async Task<IActionResult> GetClientWithRentals(int clientId)
        {
            ClientDetailsResponseDto? clientDetails = null;

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    string query = @"
                        SELECT
                            cl.ID AS ClientId, cl.FirstName, cl.LastName, cl.Address,
                            cr.ID AS RentalId, cr.DateFrom, cr.DateTo, cr.TotalPrice,
                            c.VIN, c_color.Name AS ColorName, c_model.Name AS ModelName
                        FROM clients cl
                        LEFT JOIN car_rentals cr ON cl.ID = cr.ClientID
                        LEFT JOIN cars c ON cr.CarID = c.ID
                        LEFT JOIN colors c_color ON c.ColorID = c_color.ID
                        LEFT JOIN models c_model ON c.ModelID = c_model.ID
                        WHERE cl.ID = @ClientId;";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@ClientId", clientId);
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            bool clientDataRead = false;
                            while (await reader.ReadAsync())
                            {
                                if (!clientDataRead)
                                {
                                    clientDetails = new ClientDetailsResponseDto
                                    {
                                        Id = reader.GetInt32(reader.GetOrdinal("ClientId")),
                                        FirstName = reader.GetString(reader.GetOrdinal("FirstName")),
                                        LastName = reader.GetString(reader.GetOrdinal("LastName")),
                                        Address = reader.GetString(reader.GetOrdinal("Address"))
                                    };
                                    clientDataRead = true;
                                }

                                if (clientDetails != null && !reader.IsDBNull(reader.GetOrdinal("RentalId")))
                                {
                                    var rentalInfo = new RentalInfoResponseDto
                                    {
                                        Vin = reader.IsDBNull(reader.GetOrdinal("VIN")) ? null : reader.GetString(reader.GetOrdinal("VIN")),
                                        Color = reader.IsDBNull(reader.GetOrdinal("ColorName")) ? null : reader.GetString(reader.GetOrdinal("ColorName")),
                                        Model = reader.IsDBNull(reader.GetOrdinal("ModelName")) ? null : reader.GetString(reader.GetOrdinal("ModelName")),
                                        DateFrom = reader.GetDateTime(reader.GetOrdinal("DateFrom")),
                                        DateTo = reader.GetDateTime(reader.GetOrdinal("DateTo")),
                                        TotalPrice = reader.GetInt32(reader.GetOrdinal("TotalPrice"))
                                    };
                                    clientDetails.Rentals.Add(rentalInfo);
                                }
                            }
                        }
                    }
                }

                if (clientDetails == null)
                {
                    return NotFound(new { Message = $"Client with ID {clientId} not found." });
                }

                return Ok(clientDetails);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetClientWithRentals: {ex.Message}");
                return StatusCode(500, "An internal server error occurred.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddClientWithRental([FromBody] AddClientWithRentalRequestDto request)
        {
            if (request?.Client?.FirstName == null || request.Client.LastName == null || request.Client.Address == null)
            {
                return BadRequest("Invalid client data. FirstName, LastName, and Address are required.");
            }

            if (request.DateTo <= request.DateFrom)
            {
                return BadRequest("DateTo must be after DateFrom.");
            }

            int pricePerDay;
            int newClientId;

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string carQuery = "SELECT PricePerDay FROM cars WHERE ID = @CarID;";
                    using (SqlCommand carCommand = new SqlCommand(carQuery, connection))
                    {
                        carCommand.Parameters.AddWithValue("@CarID", request.CarId);
                        var result = await carCommand.ExecuteScalarAsync();

                        if (result == null || result == DBNull.Value)
                        {
                            return BadRequest($"Car with ID {request.CarId} not found.");
                        }
                        pricePerDay = (int)result;
                    }

                    using (SqlTransaction transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            string clientInsertQuery = @"
                                INSERT INTO clients (FirstName, LastName, Address)
                                OUTPUT INSERTED.ID
                                VALUES (@FirstName, @LastName, @Address);";
                            using (SqlCommand clientCommand = new SqlCommand(clientInsertQuery, connection, transaction))
                            {
                                clientCommand.Parameters.AddWithValue("@FirstName", request.Client.FirstName);
                                clientCommand.Parameters.AddWithValue("@LastName", request.Client.LastName);
                                clientCommand.Parameters.AddWithValue("@Address", request.Client.Address);

                                var insertedIdResult = await clientCommand.ExecuteScalarAsync();
                                if (insertedIdResult == null || insertedIdResult == DBNull.Value)
                                {
                                    transaction.Rollback();
                                    return StatusCode(500, "Failed to create client and retrieve ID.");
                                }
                                newClientId = (int)insertedIdResult;
                            }

                            int days = (request.DateTo.Date - request.DateFrom.Date).Days;
                            if (days <= 0)
                            {
                                transaction.Rollback();
                                return BadRequest("Rental duration must be at least one day.");
                            }
                            int totalPrice = days * pricePerDay;

                            string rentalInsertQuery = @"
                                INSERT INTO car_rentals (ClientID, CarID, DateFrom, DateTo, TotalPrice, Discount)
                                VALUES (@ClientID, @CarID, @DateFrom, @DateTo, @TotalPrice, NULL);";
                            using (SqlCommand rentalCommand = new SqlCommand(rentalInsertQuery, connection, transaction))
                            {
                                rentalCommand.Parameters.AddWithValue("@ClientID", newClientId);
                                rentalCommand.Parameters.AddWithValue("@CarID", request.CarId);
                                rentalCommand.Parameters.AddWithValue("@DateFrom", request.DateFrom);
                                rentalCommand.Parameters.AddWithValue("@DateTo", request.DateTo);
                                rentalCommand.Parameters.AddWithValue("@TotalPrice", totalPrice);
                                await rentalCommand.ExecuteNonQueryAsync();
                            }

                            transaction.Commit();
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            Console.WriteLine($"Error during transaction: {ex.Message}");
                            return StatusCode(500, "An error occurred while processing your request.");
                        }
                    }
                }
                return CreatedAtAction(nameof(GetClientWithRentals), new { clientId = newClientId }, new { ClientId = newClientId, Message = "Client and rental created successfully." });
            }
            catch (SqlException sqlEx)
            {
                Console.WriteLine($"SQL Error in AddClientWithRental: {sqlEx.Message}");
                return StatusCode(500, "A database error occurred.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddClientWithRental: {ex.Message}");
                return StatusCode(500, "An internal server error occurred.");
            }
        }
    }
}