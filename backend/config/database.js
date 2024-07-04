// const mongoose = require('mongoose');

// const connectDatabase = () => {

//     console.log(process.env.DB_URI)
//     //Mongoose.connect(${process.env.DATABASE_URL}, { useNewUrlParser: true })
//     //mongoose.connect('mongodb://127.0.0.1:27017/Ecommerce')
// mongodb+srv://yuvraj_padhy:Yuvraj2003@cluster0.a4o7apz.mongodb.net/?retryWrites=true&w=majority&appName=Cluster0
//     mongoose.connect(${process.env.DB_URI} , { useNewUrlParser: true })
//   .then(() => console.log('Connected!'));
// }

// module.exports = connectDatabase;

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
