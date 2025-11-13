-- StudentNumber: ST10439147
-- StudentName: Dillon Rinkwest
-- CourseCode: CLDV6212
-- POE Part: 3 - SQL Database Schema

-- Create Users table for authentication
CREATE TABLE Users (
    UserId INT PRIMARY KEY IDENTITY(1,1),
    Email NVARCHAR(100) UNIQUE NOT NULL,
    PasswordHash NVARCHAR(256) NOT NULL,
    Role NVARCHAR(20) NOT NULL, -- 'Customer' or 'Admin'
    CustomerId NVARCHAR(100) NULL, -- Links to Azure Table Storage Customer RowKey
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedDate DATETIME NOT NULL DEFAULT GETUTCDATE(),
    LastLoginDate DATETIME NULL
);

-- Create index on Email for faster lookups
CREATE INDEX IX_Users_Email ON Users(Email);

-- Create index on CustomerId for faster joins
CREATE INDEX IX_Users_CustomerId ON Users(CustomerId);

-- Insert default admin account (password: Admin123!)
-- Password hash is SHA256 of "Admin123!"
INSERT INTO Users (Email, PasswordHash, Role, CustomerId, IsActive, CreatedDate)
VALUES ('admin@abcretail.com', 
        'PrP+ZrMeO00Q+nC1ytSccRIpSvauTkdqHEBRVdRaoSE=', 
        'Admin', 
        NULL, 
        1, 
        GETUTCDATE());

-- Verify the table
SELECT * FROM Users;