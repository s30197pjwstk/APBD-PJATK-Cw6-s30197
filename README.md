# Clinic REST API (ADO.NET)

Proste API REST do zarządzania wizytami w przychodni medycznej. Projekt został wykonany w ASP.NET Core Web API z użyciem wyłącznie ADO.NET (bez Entity Framework), zgodnie z wymogami zadania. 

Cała komunikacja z bazą danych opiera się na sparametryzowanych zapytaniach SQL.

## Funkcjonalności

- Pobieranie listy wizyt z filtrowaniem po statusie i nazwisku pacjenta
- Podgląd szczegółów wizyty (pacjent, lekarz, specjalizacja)
- Tworzenie nowego terminu wizyty
- Aktualizacja danych wizyty
- Usuwanie wizyty
- Walidacja reguł biznesowych

## Uruchomienie projektu

1. **Baza danych:** Uruchom skrypt `01_create_and_seed_clinic.sql` w SQL Server (np. LocalDB), aby utworzyć bazę `ClinicAdoNet` i wypełnić ją danymi.
2. **Konfiguracja:** W pliku `appsettings.json` upewnij się, że connection string `DefaultConnection` jest poprawny dla Twojego środowiska (domyślnie ustawiony na LocalDB w systemie Windows).
3. **Start:** Uruchom projekt w IDE (np. Rider / Visual Studio) lub wpisz `dotnet run` w terminalu.
