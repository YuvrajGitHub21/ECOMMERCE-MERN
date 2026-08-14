const Product = require("../models/productModel");
const ErrorHandler = require("../utils/errorHandler");

const catchAsyncError = require("../middleware/catchAsyncError");
const ApiFeatures = require("../utils/appFeature");

// Images are stored as base64 data URIs directly on the product document.
// Accepts a single string, an array of strings, or already-shaped image objects.
const toImageDocs = (rawImages) => {
  let images = [];

  if (typeof rawImages === "string") {
    images.push(rawImages);
  } else if (Array.isArray(rawImages)) {
    images = rawImages;
  }

  return images.map((image, index) => {
    if (typeof image === "string") {
      return {
        public_id: `product_${Date.now()}_${index}`,
        url: image,
      };
    }
    return image;
  });
};

//create product -- admin

exports.createProduct = catchAsyncError(async (req, res, next) => {
  const images = toImageDocs(req.body.images);

  if (images.length === 0) {
    return next(new ErrorHandler("Please add at least one product image", 400));
  }

  req.body.images = images;
  req.body.user = req.user.id;

  const product = await Product.create(req.body);

  res.status(201).json({
    success: true,
    product,
  });
});

//GET ALL PRODUCT

exports.getAllProducts = catchAsyncError(async (req, res, next) => {
    const resultPerPage = 999;

    // Get total count of products
    const productCount = await Product.countDocuments();

    // Create a new instance of ApiFeatures with the Product model and query params
    const apiFeature = new ApiFeatures(Product.find(), req.query)
        .search()
        .filter();

    // Get filtered products before pagination
    let products = await apiFeature.query.clone();
    const filteredProductsCount = products.length;

    // Apply pagination on the filtered products
    apiFeature.pagination(resultPerPage);
    products = await apiFeature.query;

    // Respond with the data
    res.status(200).json({
        success: true,
        products,
        productCount,
        resultPerPage,
        filteredProductsCount,
    });
});

// Get All Product (Admin)
exports.getAdminProducts = catchAsyncError(async (req, res, next) => {
    const products = await Product.find();

    res.status(200).json({
        success: true,
        products,
    });
});

//UPDATE Products

exports.updateProduct = catchAsyncError(async (req, res, next) => {
    let product = await Product.findById(req.params.id);

    if (!product) {
        return next(new ErrorHandler("Product not found", 404));
    }

    // Only replace images when new ones were submitted; otherwise leave the
    // existing ones untouched rather than wiping them.
    const images = toImageDocs(req.body.images);

    if (images.length > 0) {
        req.body.images = images;
    } else {
        delete req.body.images;
    }

    product = await Product.findByIdAndUpdate(req.params.id, req.body, {
        new: true,
        runValidators: true,
        useFindAndModify: false,
    });

    res.status(200).json({
        success: true,
        product,
    });
});

//DELETE Products

exports.deleteProduct = catchAsyncError(async (req, res, next) => {
    const product = await Product.findByIdAndDelete(req.params.id);

    if (!product) {
        return next(new ErrorHandler("Product not found", 404));
    }

    // Images live on the document itself, so deleting the product removes them.

    res.status(200).json({
        success: true,
        message: "Product delete successfully",
    });
});

//get product details

// exports.getProductDetails = async (req, res, next) => {

//     let product = await Product.findById(req.params.id);

//     if (!product) {
//       return next(new ErrorHandler("Product not found",404));
//     }

//     res.status(200).json({
//       success: true,
//       product
//     });

//   };

// GET PRODUCT DETAILS
exports.getProductDetails = catchAsyncError(async (req, res, next) => {
    try {
        let product = await Product.findById(req.params.id);

        if (!product) {
            return next(new ErrorHandler("Product not found", 404));
        }

        res.status(200).json({
            success: true,
            product,
        });
    } catch (error) {
        next(error); // Pass any unexpected errors to the error handling middleware
    }
});

//CREATE NEW REVIEW OR UPDATE THE REVIEW

exports.createProductReview = catchAsyncError(async (req, res, next) => {
    const { rating, comment, productId } = req.body;

    const review = {
        user: req.user._id,
        name: req.user.name,
        rating: Number(rating),
        comment,
    };

    const product = await Product.findById(productId);

    const isReviewed = product.reviews.find(
        (rev) => rev.user.toString() === req.user._id.toString()
    );

    if (isReviewed) {
        product.reviews.forEach((rev) => {
            if (rev.user.toString() === req.user._id.toString())
                (rev.rating = rating), (rev.comment = comment);
        });
    } else {
        product.reviews.push(review);
        product.numOfReviews = product.reviews.length;
    }

    let avg = 0;

    product.reviews.forEach((rev) => {
        avg += rev.rating;
    });

    product.ratings = avg / product.reviews.length;

    await product.save({ validateBeforeSave: false });

    res.status(200).json({
        success: true,
        message: "Review created successfully",
    });
});

//GET ALL REVIEWS
exports.getProductReviews = catchAsyncError(async (req, res, next) => {
    const product = await Product.findById(req.query.id);

    if (!product) {
        return next(new ErrorHandler("Product not found", 404));
    }

    res.status(200).json({
        success: true,
        reviews: product.reviews,
    });
});

//DELETE REVIEW

exports.deleteReview = catchAsyncError(async (req, res, next) => {
    const product = await Product.findById(req.query.productId);

    if (!product) {
        return next(new ErrorHandler("Product not found", 404));
    }

    const reviews = product.reviews.filter(
        (rev) => rev._id.toString() !== req.query.id.toString()
    );

    let avg = 0;

    reviews.forEach((rev) => {
        avg += rev.rating;
    });

    let ratings = 0;

    if (reviews.length === 0) {
        ratings = 0;
    } else {
        ratings = avg / reviews.length;
    }

    const numOfReviews = reviews.length;

    await Product.findByIdAndUpdate(
        req.query.productId,
        {
            reviews,
            ratings,
            numOfReviews,
        },
        {
            new: true,
            runValidators: true,
            useFindAndModify: false,
        }
    );

    res.status(200).json({
        success: true,
    });
});
