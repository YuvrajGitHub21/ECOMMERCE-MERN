const app = require("./app");

const connectDatabase = require("./config/database");


// Handling uncaught exception

process.on("uncaughtException", (err) => {
    console.log(`Error : ${err.message}`);
    console.log(`Shutting down the server due to unhandled Exception `);
    process.exit(1);
});

//Config
if (process.env.NODE_ENV !== "PRODUCTION") {
    require("dotenv").config({ path: "backend/config/config.env" });
  }

// Connecting to database

connectDatabase();

const PORT = process.env.PORT || 4000;

const server = app.listen(PORT, () => {
    console.log(`Server is working on port ${PORT}`);
});

// Unhandled Promise Rejection
process.on("unhandledRejection", (err) => {
    console.log(`Error: ${err.message}`);
    console.log(`Shutting down the server due to Unhandled Promise Rejection`);

    server.close(() => {
        process.exit(1);
    });
});
