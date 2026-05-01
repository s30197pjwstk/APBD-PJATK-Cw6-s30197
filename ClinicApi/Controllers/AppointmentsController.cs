using ClinicApi.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ClinicApi.Controllers;[Route("api/[controller]")]
[ApiController]
public class AppointmentsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;

    public AppointmentsController(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string is missing.");
    }

    // GET: /api/appointments
    [HttpGet]
    public async Task<IActionResult> GetAppointments([FromQuery] string? status, [FromQuery] string? patientLastName)
    {
        var appointments = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var query = @"
            SELECT 
                a.IdAppointment, 
                a.AppointmentDate, 
                a.Status, 
                a.Reason, 
                p.FirstName + N' ' + p.LastName AS PatientFullName, 
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = (object?)status ?? DBNull.Value;
        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 80).Value = (object?)patientLastName ?? DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return Ok(appointments);
    }

    // GET: /api/appointments/{id}
    [HttpGet("{idAppointment}")]
    public async Task<IActionResult> GetAppointmentDetails(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var query = @"
            SELECT 
                a.IdAppointment, a.AppointmentDate, a.Status, a.Reason, a.InternalNotes, a.CreatedAt,
                p.FirstName + N' ' + p.LastName AS PatientFullName, p.Email AS PatientEmail, p.PhoneNumber AS PatientPhone,
                d.FirstName + N' ' + d.LastName AS DoctorFullName, d.LicenseNumber AS DoctorLicense,
                s.Name AS SpecializationName
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON a.IdPatient = p.IdPatient
            JOIN dbo.Doctors d ON a.IdDoctor = d.IdDoctor
            JOIN dbo.Specializations s ON d.IdSpecialization = s.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return NotFound(new ErrorResponseDto { Message = "Appointment not found." });
        }

        var details = new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes")) ? null : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhone")),
            DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicense")),
            DoctorSpecialization = reader.GetString(reader.GetOrdinal("SpecializationName"))
        };

        return Ok(details);
    }

    // POST: /api/appointments
    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
    {
        if (request.AppointmentDate < DateTime.UtcNow)
        {
            return BadRequest(new ErrorResponseDto { Message = "Appointment date cannot be in the past." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
        {
            return BadRequest(new ErrorResponseDto { Message = "Reason is required and must be max 250 characters." });
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        if (!await IsPatientActive(connection, request.IdPatient))
            return BadRequest(new ErrorResponseDto { Message = "Patient does not exist or is inactive." });

        if (!await IsDoctorActive(connection, request.IdDoctor))
            return BadRequest(new ErrorResponseDto { Message = "Doctor does not exist or is inactive." });

        if (await HasDoctorConflict(connection, request.IdDoctor, request.AppointmentDate))
        {
            return Conflict(new ErrorResponseDto { Message = "Doctor already has an appointment at this specific time." });
        }

        var insertQuery = @"
            INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
            OUTPUT INSERTED.IdAppointment
            VALUES (@IdPatient, @IdDoctor, @AppointmentDate, N'Scheduled', @Reason);";

        await using var command = new SqlCommand(insertQuery, connection);
        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;

        var newId = (int)await command.ExecuteScalarAsync()!;

        return Created($"/api/appointments/{newId}", new { IdAppointment = newId });
    }

    // PUT: /api/appointments/{id}
    [HttpPut("{idAppointment}")]
    public async Task<IActionResult> UpdateAppointment(int idAppointment, [FromBody] UpdateAppointmentRequestDto request)
    {
        string[] validStatuses = { "Scheduled", "Completed", "Cancelled" };
        if (!validStatuses.Contains(request.Status))
        {
            return BadRequest(new ErrorResponseDto { Message = "Invalid status." });
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var currentAppointmentQuery = "SELECT Status, AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @IdAppointment";
        await using var currentCmd = new SqlCommand(currentAppointmentQuery, connection);
        currentCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        string? currentStatus = null;
        DateTime? currentApptDate = null;

        await using (var reader = await currentCmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                return NotFound(new ErrorResponseDto { Message = "Appointment not found." });
            }
            currentStatus = reader.GetString(0);
            currentApptDate = reader.GetDateTime(1);
        }

        if (currentStatus == "Completed" && currentApptDate != request.AppointmentDate)
        {
            return Conflict(new ErrorResponseDto { Message = "Cannot change the date of a completed appointment." });
        }

        if (!await IsPatientActive(connection, request.IdPatient))
            return BadRequest(new ErrorResponseDto { Message = "Patient does not exist or is inactive." });

        if (!await IsDoctorActive(connection, request.IdDoctor))
            return BadRequest(new ErrorResponseDto { Message = "Doctor does not exist or is inactive." });

        if (await HasDoctorConflict(connection, request.IdDoctor, request.AppointmentDate, idAppointment))
        {
            return Conflict(new ErrorResponseDto { Message = "Doctor already has an appointment at this specific time." });
        }

        var updateQuery = @"
            UPDATE dbo.Appointments
            SET IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;";

        await using var updateCmd = new SqlCommand(updateQuery, connection);
        updateCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        updateCmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
        updateCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        updateCmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        updateCmd.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = request.Status;
        updateCmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;
        updateCmd.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, 500).Value = (object?)request.InternalNotes ?? DBNull.Value;

        await updateCmd.ExecuteNonQueryAsync();

        return Ok();
    }

    // DELETE: /api/appointments/{id}
    [HttpDelete("{idAppointment}")]
    public async Task<IActionResult> DeleteAppointment(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var statusQuery = "SELECT Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;";
        await using var statusCmd = new SqlCommand(statusQuery, connection);
        statusCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        var statusObj = await statusCmd.ExecuteScalarAsync();
        if (statusObj == null)
        {
            return NotFound(new ErrorResponseDto { Message = "Appointment not found." });
        }

        string currentStatus = (string)statusObj;
        if (currentStatus == "Completed")
        {
            return Conflict(new ErrorResponseDto { Message = "Cannot delete a completed appointment." });
        }

        var deleteQuery = "DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;";
        await using var deleteCmd = new SqlCommand(deleteQuery, connection);
        deleteCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await deleteCmd.ExecuteNonQueryAsync();

        return NoContent();
    }


    private async Task<bool> IsPatientActive(SqlConnection connection, int patientId)
    {
        var query = "SELECT IsActive FROM dbo.Patients WHERE IdPatient = @Id";
        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = patientId;
        var result = await cmd.ExecuteScalarAsync();
        return result != null && (bool)result;
    }

    private async Task<bool> IsDoctorActive(SqlConnection connection, int doctorId)
    {
        var query = "SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @Id";
        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = doctorId;
        var result = await cmd.ExecuteScalarAsync();
        return result != null && (bool)result;
    }

    private async Task<bool> HasDoctorConflict(SqlConnection connection, int doctorId, DateTime appointmentDate, int? excludeAppointmentId = null)
    {
        var query = @"
            SELECT COUNT(1) 
            FROM dbo.Appointments 
            WHERE IdDoctor = @IdDoctor 
              AND AppointmentDate = @Date
              AND (@ExcludeId IS NULL OR IdAppointment != @ExcludeId);";

        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = doctorId;
        cmd.Parameters.Add("@Date", SqlDbType.DateTime2).Value = appointmentDate;
        cmd.Parameters.Add("@ExcludeId", SqlDbType.Int).Value = (object?)excludeAppointmentId ?? DBNull.Value;

        var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        return count > 0;
    }
}