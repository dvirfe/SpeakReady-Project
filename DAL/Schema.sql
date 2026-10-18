-- SpeakReady: שבע הטבלאות של מסמך הייזום, בסדר התלויות.
-- הקובץ מוטמע ב-DAL ומורץ אוטומטית. אין כאן מחיקה של נתונים.
CREATE TABLE IF NOT EXISTS Users (
    UserId INT AUTO_INCREMENT PRIMARY KEY,
    Username VARCHAR(50) NOT NULL UNIQUE,
    FullName VARCHAR(100) NOT NULL,
    Email VARCHAR(254) NOT NULL UNIQUE,
    PasswordHash VARCHAR(255) NOT NULL,
    Role ENUM('Student','Teacher','Admin') NOT NULL DEFAULT 'Student',
    TeacherRequestStatus ENUM('None','Pending','Approved','Rejected') NOT NULL DEFAULT 'None',
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS Lessons (
    LessonId INT AUTO_INCREMENT PRIMARY KEY,
    Title VARCHAR(150) NOT NULL,
    Content TEXT NOT NULL,
    SortOrder INT NOT NULL DEFAULT 0,
    IsPublished BOOLEAN NOT NULL DEFAULT FALSE,
    IsDemo BOOLEAN NOT NULL DEFAULT FALSE,
    CreatedByUserId INT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CreatedByUserId) REFERENCES Users(UserId) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS Exercises (
    ExerciseId INT AUTO_INCREMENT PRIMARY KEY,
    LessonId INT NOT NULL,
    PromptText TEXT NOT NULL,
    Instructions TEXT NOT NULL,
    MediaUrl VARCHAR(500) NULL,
    SortOrder INT NOT NULL DEFAULT 0,
    MaxSeconds INT NOT NULL DEFAULT 120,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CHECK (MaxSeconds > 0),
    FOREIGN KEY (LessonId) REFERENCES Lessons(LessonId) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS StudyGroups (
    GroupId INT AUTO_INCREMENT PRIMARY KEY,
    GroupName VARCHAR(100) NOT NULL,
    TeacherId INT NOT NULL,
    JoinCode VARCHAR(32) COLLATE utf8mb4_bin NOT NULL UNIQUE,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (TeacherId) REFERENCES Users(UserId) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS GroupMembers (
    GroupId INT NOT NULL,
    StudentId INT NOT NULL,
    JoinedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    PRIMARY KEY (GroupId, StudentId),
    FOREIGN KEY (GroupId) REFERENCES StudyGroups(GroupId) ON DELETE RESTRICT,
    FOREIGN KEY (StudentId) REFERENCES Users(UserId) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS Assignments (
    AssignmentId INT AUTO_INCREMENT PRIMARY KEY,
    GroupId INT NOT NULL,
    ExerciseId INT NOT NULL,
    AssignedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    DueAt DATETIME NOT NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE KEY UQ_AssignmentExercise (AssignmentId, ExerciseId),
    FOREIGN KEY (GroupId) REFERENCES StudyGroups(GroupId) ON DELETE RESTRICT,
    FOREIGN KEY (ExerciseId) REFERENCES Exercises(ExerciseId) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS PracticeAttempts (
    AttemptId INT AUTO_INCREMENT PRIMARY KEY,
    StudentId INT NOT NULL,
    ExerciseId INT NOT NULL,
    AssignmentId INT NULL,
    SubmissionKey CHAR(36) NOT NULL UNIQUE,
    AudioPath VARCHAR(500) NOT NULL,
    DurationSeconds INT NOT NULL,
    SubmittedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    AnalysisStatus ENUM('Pending','Processing','Completed','Failed','Unintelligible') NOT NULL DEFAULT 'Pending',
    Transcript TEXT NULL,
    AIFeedback TEXT NULL,
    AnalyzedAt DATETIME NULL,
    AnalysisError VARCHAR(255) NULL,
    TeacherFeedback TEXT NULL,
    ReviewedByUserId INT NULL,
    ReviewedAt DATETIME NULL,
    CHECK (DurationSeconds > 0),
    FOREIGN KEY (StudentId) REFERENCES Users(UserId) ON DELETE RESTRICT,
    FOREIGN KEY (ExerciseId) REFERENCES Exercises(ExerciseId) ON DELETE RESTRICT,
    FOREIGN KEY (AssignmentId, ExerciseId) REFERENCES Assignments(AssignmentId, ExerciseId) ON DELETE RESTRICT,
    FOREIGN KEY (ReviewedByUserId) REFERENCES Users(UserId) ON DELETE RESTRICT
) ENGINE=InnoDB;

