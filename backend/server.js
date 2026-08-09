const app = require("./app");

const cloudinary = require("cloudinary");

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

cloudinary.config({
    cloud_name: process.env.CLOUDINARY_NAME,
    api_key: process.env.CLOUDINARY_API_KEY,
    api_secret: process.env.CLOUDINARY_API_SECRET,
});

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
