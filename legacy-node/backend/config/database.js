const mongoose = require("mongoose");

const connectDatabase = () => {
    mongoose
        .connect(process.env.DB_URI)
        .then(() => console.log("Connected to the database!"))
        .catch((err) => {
            console.error("Failed to connect to the database:", err);
        });
};

module.exports = connectDatabase;
